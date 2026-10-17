# Vehicle protocol (for the Pi)

This is the contract between the vehicle (Raspberry Pi) and the cloud. Build against this page and
`src/SmartCity.Contracts/Protos/vehicle.proto`. The design behind it (in Arabic) is
`vehicle-communication-design.md` next to this file.

Two channels:

| Channel | Used for |
|---|---|
| **gRPC** (HTTP/2) | Camera frames: the image and its data in one message. The reply is the proof the frame was saved. |
| **MQTT** (EMQX) | Small messages only: heartbeat (telemetry), online/offline, server → vehicle commands. Never images. |

All times are UTC. Coordinates are WGS84.

---

## 1. Frames over gRPC

- Service: `smartcity.vehicle.v1.VehicleIngestService`, method `UploadFrame`.
- Address (dev): `http://<server>:5211`, HTTP/2 without TLS. Keep one channel open and send every frame on it.
- Max message size: 5 MB. The image itself must be at most 4 MB.
- Set a deadline of 30 seconds per call.
- **Vehicle id:** not in the message. In production it comes from the client certificate (mTLS).
  In dev, send it as gRPC metadata: `x-vehicle-id: MUN-VEH-001`.

### Request fields

| Field | Type | Value |
|---|---|---|
| `frame_id` | string | A UUID the Pi makes when it takes the photo. A resend of the same frame **must** use the same id. |
| `captured_at_utc` | int64 | When the photo was taken, Unix epoch milliseconds, UTC. |
| `latitude`, `longitude` | double | Location at capture time. |
| `gps_accuracy_m` | double | GPS accuracy in meters. |
| `gps_satellites` | int32 | Satellites in the fix. |
| `heading_deg` | double | 0–360, 0 = north. |
| `speed_kmh` | double | Speed in km/h. |
| `image` | bytes | The JPEG. |
| `content_type` | string | `image/jpeg` |
| `width_px`, `height_px` | int32 | Image size. |

### Replies: the frame is finished, delete it from the card

| `result` | Meaning | What the Pi does |
|---|---|---|
| `SAVED` | Saved now. | Delete the frame. |
| `DUPLICATE` | Saved before (same `frame_id`). | Delete the frame. |
| `REJECTED` + `reject_reason` | It will never be accepted. | Delete the frame. Log the reason. |

| `reject_reason` | Cause |
|---|---|
| `OUTSIDE_SERVICE_AREA` | Location outside Istanbul (lat 40.80–41.60, lon 27.95–29.98). |
| `GPS_INACCURATE` | `gps_accuracy_m` worse than 25 m. |
| `IMAGE_SIZE` | Image empty or larger than 4 MB. |
| `IMAGE_TYPE` | `content_type` is not `image/jpeg`. |
| `BAD_TIMESTAMP` | More than 5 minutes in the future, or older than `frameTtlHours` (24 h). Check the Pi clock (NTP / GPS time). |
| `BAD_FRAME_ID` | `frame_id` is not a UUID. |

### Errors (gRPC status): the frame is NOT finished, keep it on the card

| Status | Meaning | What the Pi does |
|---|---|---|
| `UNAVAILABLE` | Storage or something inside the server is down. | Keep the frame. Retry later with backoff (1 s, 2 s, 4 s … max 30 s). |
| `RESOURCE_EXHAUSTED` | Sending too fast (more than 5 frames per second). | Keep the frame. Slow down, then retry with backoff. |
| `UNAUTHENTICATED` | The server does not know this vehicle. | Keep the frame. **Stop uploading.** Try the oldest frame again once every 60 s. |
| `PERMISSION_DENIED` | The vehicle was suspended or retired by the admin. | Keep the frame. **Stop uploading.** Try the oldest frame again once every 60 s. |
| No reply (network down, deadline exceeded, any other error) | Unknown. | Keep the frame. Retry later with backoff. |

---

## 2. MQTT

- Broker: EMQX, `<server>:1883` (dev, no TLS). Production adds mTLS on 8883.
- MQTT 5. Client id = the vehicle id (`MUN-VEH-001`). Keep-alive 15 s.
- QoS 1 on every topic. JSON payloads, UTF-8, camelCase.

### Topics

| Topic | Direction | Retained | When |
|---|---|---|---|
| `vehicles/{id}/telemetry` | Pi → server | no | Every 2 seconds, moving or stopped. |
| `vehicles/{id}/status` | Pi → server (and EMQX, from the Last Will) | **yes** | `online` right after connecting; `offline` by EMQX when the Pi disappears. |
| `fleet/status` | server → all Pis | **yes** | When uploads must pause or may run. |
| `vehicles/{id}/config` | server → Pi | **yes** | Capture settings. |

Retained means EMQX keeps the last message on the topic. A Pi that connects later gets it at once,
so after connecting the Pi always knows the current `fleet/status` and its own `config`.

### Connect sequence

1. Connect with the **Last Will** set to topic `vehicles/{id}/status`, payload `{"state":"offline"}`,
   QoS 1, **retained**. EMQX publishes it by itself if the Pi drops off (about 1.5 × keep-alive).
2. Publish `{"state":"online"}` to `vehicles/{id}/status`, QoS 1, **retained**.
3. Subscribe to `fleet/status` and `vehicles/{id}/config` (QoS 1).
4. Start telemetry. Start uploading frames unless `fleet/status` says `paused`.

### `vehicles/{id}/telemetry`

```json
{
  "capturedAt": "2026-10-17T09:30:00.000Z",
  "location": { "latitude": 40.985, "longitude": 29.030, "accuracyMeters": 2.5, "heading": 87.0 },
  "speedKmh": 32.5,
  "moving": true,
  "pendingFrames": 12,
  "freeStorageMb": 18432,
  "signalStrengthDbm": -75,
  "networkType": "4G",
  "batteryPercent": 92.0,
  "cameraOnline": true,
  "routeId": "ROUTE-01",
  "softwareVersion": "1.0.0"
}
```

`capturedAt` and `location.latitude` / `location.longitude` are required. `pendingFrames` is the
number of frames waiting on the card; the admin screen shows it. The rest may be left out.

### `vehicles/{id}/status`

```json
{"state":"online"}
```
```json
{"state":"offline"}
```

### `fleet/status`

```json
{"state":"paused","reason":"storage","retryAfterSeconds":30}
```
```json
{"state":"running"}
```

On `paused`: stop uploading, keep capturing and saving to the card. On `running`: upload again,
oldest first. `retryAfterSeconds` is a hint only; wait for `running`.

### `vehicles/{id}/config`

```json
{
  "captureEveryMeters": 10,
  "maxSecondsBetweenFrames": 10,
  "skipWhenStopped": true,
  "jpegQuality": 85,
  "resolution": "1920x1080",
  "frameTtlHours": 24,
  "maxUploadsPerSecond": 2
}
```

| Field | Meaning |
|---|---|
| `captureEveryMeters` | Take a photo every N meters driven. |
| `maxSecondsBetweenFrames` | While moving, at least one photo every N seconds (for slow traffic). |
| `skipWhenStopped` | No photos while the vehicle is stopped. |
| `jpegQuality`, `resolution` | JPEG settings. |
| `frameTtlHours` | Delete frames older than this without sending them. |
| `maxUploadsPerSecond` | Upload speed limit, mostly for catching up after a long offline period. |

Apply a new config as soon as it arrives.

---

## 3. Rules for the Pi

1. **Card first.** Every photo is written to the SD card and put in the upload queue before any
   upload is tried.
2. **Delete only on a reply.** Delete a frame only on `SAVED`, `DUPLICATE` or `REJECTED`. On any
   error or no reply, keep it.
3. **Oldest first.** Upload the queue in capture order. New photos go to the end.
4. **Speed limit.** At most `maxUploadsPerSecond` uploads per second.
5. **Time limit.** Delete, without sending, any frame older than `frameTtlHours` (from `captured_at_utc`).
6. **Pause.** While `fleet/status` is `paused`, do not upload. Keep capturing.
7. **Blocked.** After `UNAUTHENTICATED` or `PERMISSION_DENIED`, stop uploading. Try the oldest frame
   once every 60 s until it is accepted (the admin may resume the vehicle) or the frames expire.
8. **Same id on resend.** A frame keeps its `frame_id` forever, so a resend after a lost reply comes
   back as `DUPLICATE` and is not saved twice.
