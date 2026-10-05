# شرح PR #1 — Milestone 1: خط أنابيب الاستقبال (Ingestion Pipeline)

> **الملخص:** أول حاجة بتحصل في النظام كله: العربية بتبعت صورة → السيرفر بيستلمها → بيرفعها على S3 → بيبعت إيفنت يقول "صورة وصلت".

---

## الجزء الأول: الملفين الأساسيين

---

### ملف 1 (الأهم): `MqttSubscriberService.cs` — "الودان بتاعة السيرفر"

**المسار:** `src/Services/VehicleCommunication/SmartCity.VehicleCommunication/Mqtt/MqttSubscriberService.cs`

> **في جملة:** الخدمة دي بتسمع على MQTT broker وكل ما عربية تبعت صورة أو بيانات (telemetry)، بتستلمها، بترفع الصورة على S3، وبتنشر إيفنت على RabbitMQ يقول "صورة جديدة وصلت".

**بيكلّم مين:** بيستلم من EMQX (MQTT broker)، بيكلّم SeaweedFS (S3)، بيكلّم RabbitMQ (عبر MassTransit)، وبيكلّم Redis.

#### الأجزاء المهمة سطر سطر:

**التعريف والكونستركتور (سطر 16-28):**

```csharp
public sealed class MqttSubscriberService : BackgroundService
```

`BackgroundService` يعني "شغّلني في الخلفية أول ما البرنامج يفتح". زي واحد قاعد في الكول سنتر 24 ساعة مستني حد يرن عليه.

```csharp
private readonly IServiceProvider _services;
```

بيخزّن الـ `IServiceProvider` عشان بعد كده يقدر يطلب أي خدمة (S3, MassTransit, Redis) لما يحتاجها. مش بياخدهم في الكونستركتور مباشرة عشان `BackgroundService` عمره طويل (singleton) بس الخدمات التانية عمرها قصير (scoped).

**الاتصال بالـ MQTT (سطر 30-76) — `ExecuteAsync`:**

```csharp
var mqttHost = _config["Mqtt:Host"] ?? "localhost";
var mqttPort = _config.GetValue("Mqtt:Port", 1883);
```

بياخد عنوان البروكر من الإعدادات. لو مش موجود، بيستخدم `localhost:1883`.

```csharp
.WithClientId($"vehiclecomm-{Environment.MachineName}-{Guid.NewGuid():N}"[..30])
.WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
```

كل مشترك MQTT لازم يكون ليه اسم فريد. الـ `[..30]` بيقص الاسم لـ 30 حرف (حد أقصى). واستخدم MQTT v5 عشان يدعم الـ User Properties (هنشوفها بعدين).

```csharp
_client.DisconnectedAsync += async e =>
{
    _logger.LogWarning("MQTT disconnected...");
    await Task.Delay(3000, stoppingToken);
    try { await _client.ConnectAsync(options, stoppingToken); }
    catch (Exception ex) { _logger.LogError(ex, "MQTT reconnect failed"); }
};
```

لو الاتصال اتقطع (النت وقع مثلاً) → استنى 3 ثواني → حاول تاني. ده بيخلي الخدمة مش بتموت لما البروكر يعمل restart.

```csharp
.WithTopicFilter("$share/vehiclecomm/vehicles/+/frames", ...)
.WithTopicFilter("$share/vehiclecomm/vehicles/+/telemetry", ...)
```

**ده مهم جداً.** الـ `$share/vehiclecomm/` معناها "shared subscription" — لو شغّلت 3 instances من الخدمة، كل رسالة هتروح لواحد بس (load balancing). الـ `+` معناها "أي عربية" — يعني `vehicles/MUN-VEH-001/frames` و `vehicles/MUN-VEH-002/frames` كلهم هيتلقطوا.

**استقبال الرسالة (سطر 78-94) — `OnMessageReceived`:**

```csharp
var topic = e.ApplicationMessage.Topic;      // مثلاً "vehicles/MUN-VEH-001/frames"
var segments = topic.Split('/');              // ["vehicles", "MUN-VEH-001", "frames"]
var vehicleId = segments[1];                 // "MUN-VEH-001"
var messageType = segments[2];               // "frames" أو "telemetry"
```

ببساطة بيفكّك اسم الـ topic عشان يعرف الرسالة دي من أنهي عربية ونوعها إيه.

**معالجة الصورة (سطر 96-179) — `HandleFrame`:**

```csharp
if (!IsValidVehicleId(vehicleId))
```

أول حاجة: هل الـ ID بتاع العربية شكله صح؟ لازم يبدأ بـ `MUN-VEH-`.

```csharp
var props = msg.UserProperties?.ToDictionary(p => p.Name, p => p.Value)
```

**ده الـ User Properties بتاع MQTT v5.** تخيل كل رسالة MQTT عبارة عن ظرف: الصورة جوا الظرف (payload)، والمعلومات (frameId, lat, lon, capturedAt) مكتوبة على الظرف من برا (user properties). ده عشان متحتاجش تفتح الظرف عشان تعرف المعلومات.

```csharp
if (!props.TryGetValue("frameId", out var frameIdStr) || ...)
```

بيتأكد إن كل البيانات المطلوبة موجودة. لو حاجة ناقصة → بيلوّج warning ومش بيكمل.

```csharp
// Konya bounding box validation
if (lat < 37.6 || lat > 38.2 || lon < 32.2 || lon > 32.8)
```

**فحص جغرافي:** المشروع ده لمدينة قونيا في تركيا. لو الإحداثيات برا المدينة → ارفضها. ده عشان لو عربية بعتت بيانات غلط أو حد حاول يبعت بيانات مزيفة.

```csharp
var s3Key = $"{vehicleId}/{dateKey}/{frameId}.jpg";
// مثلاً: "MUN-VEH-001/2026-10-02/abc123.jpg"

await s3.PutObjectAsync(new PutObjectRequest
{
    BucketName = "frames-temp",
    Key = s3Key,
    InputStream = stream,
    ContentType = contentType ?? "image/jpeg",
});
```

**هنا بيرفع الصورة على S3.** بيحطها في بكت اسمه `frames-temp` (مؤقت). الـ key منظم: عربية/تاريخ/frameId. كده بسهولة تلاقي كل صور عربية معينة في يوم معين.

```csharp
var imageUrl = $"{s3Options.PublicBaseUrl}/frames-temp/{s3Key}";
```

بيبني الـ URL العلني بتاع الصورة. ده اللي هيتبعت في الإيفنت — مش الصورة نفسها، **لينك الصورة بس** (حسب قاعدة المعمارية: "messages carry URLs, never image bytes").

```csharp
var frameReceived = new FrameReceived
{
    FrameId = frameId,
    CorrelationId = frameId,    // أول إيفنت في السلسلة → CorrelationId = FrameId
    VehicleId = vehicleId,
    ImageUrl = imageUrl,        // لينك الصورة مش الصورة نفسها
    ...
};
await publishEndpoint.PublishEvent(frameReceived);
```

**ده أهم سطر في الملف.** بينشر إيفنت `FrameReceived` على RabbitMQ. الإيفنت ده هو اللي هيخلي خدمة الـ AI Detection تشتغل (في PR #2). لاحظ إن الـ `CorrelationId = frameId` — ده بيربط كل الأحداث اللي هتحصل بعد كده (اكتشاف حفرة، حساب تكلفة، أمر شغل) بالصورة الأصلية.

**معالجة التيليمتري (سطر 181-241) — `HandleTelemetry`:**

```csharp
var json = JsonDocument.Parse(msg.PayloadSegment);
```

التيليمتري بتيجي كـ JSON عادي (مش MQTT user properties زي الصورة). بيفككها ويطلع منها (سرعة، موقع، بطارية، حالة الكاميرا...).

```csharp
await publishEndpoint.PublishEvent(telemetryEvent);
```

بينشر إيفنت `VehicleTelemetryReceived` على الباص.

```csharp
var db = redis.GetDatabase();
var hashKey = $"vehicle:pos:{vehicleId}";
await db.HashSetAsync(hashKey, new HashEntry[] { ... });
await db.KeyExpireAsync(hashKey, TimeSpan.FromSeconds(60));
```

**ده بونص:** بيحفظ آخر موقع لكل عربية في Redis كـ hash. والـ `KeyExpireAsync(60)` يعني لو العربية ما بعتتش حاجة لمدة 60 ثانية، البيانات تتمسح (يبقى الموقع قديم). ده عشان الخريطة الحية (live map) في الفرونت إند تقدر تعرض مواقع العربيات بسرعة من غير ما تروح للداتابيز.

---

### ملف 2: `VehicleSimulator/Program.cs` — "العربية الوهمية"

**المسار:** `tools/VehicleSimulator/Program.cs`

> **في جملة:** أداة سطر أوامر بتعمل نفسها عربيات بلدية بتبعت صور وبيانات تيليمتري على MQTT كل شوية — عشان نجرّب النظام من غير عربيات حقيقية.

**بيكلّم مين:** بيبعت لـ EMQX (MQTT broker) بس. ده المُرسِل، الملف اللي فوق هو المُستقبِل.

#### الأجزاء المهمة:

**الباراميترات (سطر 7-23):**

```csharp
var vehicles = 5;         // عدد العربيات الوهمية
var intervalMs = 2000;    // كل 2 ثانية ابعت فريم
var frames = int.MaxValue; // عدد لا نهائي (شغّال لحد ما توقفه)

for (var i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--vehicles": vehicles = int.Parse(args[++i]); break;
        ...
    }
```

بتقدر تشغله كده: `dotnet run --project tools/VehicleSimulator -- --vehicles 3 --interval-ms 1000`

**الصورة الوهمية (سطر 27-33):**

```csharp
// SKELETON: real version uses real camera frames; this is a 1x1 placeholder JPEG.
var tinyJpeg = Convert.FromBase64String("...");
```

بدل ما يبعت صورة حقيقية (اللي هتيجي من كاميرا العربية)، بيبعت صورة JPEG ضغيرة 1×1 بكسل. ده عشان إحنا في مرحلة الـ skeleton — المهم إن السلسلة كلها تشتغل end-to-end.

**المسارات الوهمية (سطر 35-44):**

```csharp
var routes = new (double lat, double lon)[][]
{
    // Meram loop (~37.85, 32.43)
    new[] { (37.850, 32.430), (37.851, 32.432), ... },
    // Selcuklu loop
    ...
};
```

3 مسارات دائرية في 3 أحياء في قونيا. كل عربية بتلف في مسار (بتتحرك من نقطة للتانية وبعدين ترجع). ده بيحاكي العربيات وهي بتلف في الشوارع.

**بناء رسالة الصورة (سطر 73-93):**

```csharp
var frameMsg = new MqttApplicationMessageBuilder()
    .WithTopic($"vehicles/{vehicleId}/frames")       // الـ topic: vehicles/MUN-VEH-001/frames
    .WithPayload(tinyJpeg)                            // الصورة كـ bytes
    .WithQualityOfServiceLevel(AtLeastOnce)           // QoS 1: اضمنلي إنها توصل مرة على الأقل
    .WithUserProperty("frameId", frameId.ToString())  // metadata على الظرف
    .WithUserProperty("lat", pos.lat.ToString("F6"))
    ...
```

**لاحظ:** الصورة (bytes) في الـ `payload`، والمعلومات (frameId, lat, lon...) في الـ `UserProperties`. ده نفس الشكل اللي الـ `MqttSubscriberService` بيستناه.

**بناء رسالة التيليمتري (سطر 95-114):**

```csharp
var telemetry = new {
    vehicleId,
    location = new { latitude = pos.lat, longitude = pos.lon, ... },
    speedKmh = speed,
    batteryPercent = 70.0 + rng.NextDouble() * 30.0,
    cameraOnline = true,
    ...
};
var telemetryMsg = new MqttApplicationMessageBuilder()
    .WithTopic($"vehicles/{vehicleId}/telemetry")
    .WithPayload(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(telemetry)))
```

التيليمتري بتتبعت كـ JSON عادي على topic مختلف (`/telemetry` بدل `/frames`).

**الإرسال (سطر 116-121):**

```csharp
tasks.Add(client.PublishAsync(frameMsg, cts.Token));     // ابعت الصورة
tasks.Add(client.PublishAsync(telemetryMsg, cts.Token));  // ابعت التيليمتري
...
await Task.WhenAll(tasks);        // استنى كل العربيات تبعت
await Task.Delay(intervalMs);     // استنى الفترة المحددة وابدأ تاني
```

كل tick: كل العربيات بتبعت في نفس الوقت (parallel)، وبعدين بيستنى `intervalMs` مللي ثانية قبل الـ tick اللي بعده.

---

## الجزء التاني: الملفات المساعدة

---

### ملف 3: `VehicleCommunication/Program.cs` — "تسجيل الحضور"

**المسار:** `src/Services/VehicleCommunication/SmartCity.VehicleCommunication/Program.cs`

> **في جملة:** بيسجّل الخدمات الجديدة (Redis و MQTT subscriber) في الـ DI container عشان البرنامج يعرف يشغّلهم.

**بيكلّم مين:** ده ملف الإعداد بتاع خدمة VehicleCommunication — مش بيكلّم حد، بس بيقول للبرنامج "لما تحتاج Redis هتلاقيه هنا، ولما تفتح شغّل الـ MQTT subscriber في الخلفية".

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));
```

بيعمل اتصال واحد بـ Redis ويخليه `Singleton` (يعني اتصال واحد للبرنامج كله، مش كل request يفتح اتصال جديد). لو مفيش إعداد في الـ config، بيستخدم `localhost:6379`.

```csharp
builder.Services.AddHostedService<MqttSubscriberService>();
```

**ده السطر السحري.** بيقول لـ ASP.NET: "لما البرنامج يشتغل، شغّل `MqttSubscriberService` في الخلفية". من غير السطر ده، الملف اللي شرحناه فوق (الـ 286 سطر) ما كانش هيشتغل أصلاً.

```csharp
await app.EnsureBucketsAsync("frames-temp", "frames-permanent", "evidence");
```

ده كان موجود قبل كده — بيتأكد إن الـ S3 buckets موجودة لما البرنامج يفتح.

---

### ملف 4: `infra/s3/s3.json` — "بطاقة الهوية بتاعة S3"

**المسار:** `infra/s3/s3.json`

> **في جملة:** ملف بيعرّف SeaweedFS مين مسموح له يستخدم الـ S3 وإيه الصلاحيات بتاعته.

```json
{
  "identities": [
    {
      "name": "dev",
      "credentials": [{ "accessKey": "dev", "secretKey": "dev" }],
      "actions": ["Admin", "Read", "Write", "List", "Tagging"]
    },
    {
      "name": "anonymous",
      "actions": ["Read", "List"]
    }
  ]
}
```

فيه يوزرين:

- **`dev`**: بيقدر يعمل كل حاجة (قراءة، كتابة، مسح). ده اللي الكود بتاعنا بيستخدمه لما يرفع صور.
- **`anonymous`**: بيقدر يقرأ بس. ده عشان لما تفتح لينك صورة في البراوزر، تشتغل من غير ما تحتاج credentials.

---

### ملف 5: `infra/s3/init-buckets.sh` — "تجهيز الشنط"

**المسار:** `infra/s3/init-buckets.sh`

> **في جملة:** سكريبت بيتشغل مرة واحدة لما Docker يقوم، بيعمل الـ S3 buckets وبيحط عليهم lifecycle policy.

التغيير الأساسي: **ضاف authentication** لأوامر curl:

```bash
AWS_ACCESS_KEY_ID="${S3_ACCESS_KEY:-dev}"
AWS_SECRET_ACCESS_KEY="${S3_SECRET_KEY:-dev}"
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
```

بيعرّف الـ credentials (dev/dev) كـ environment variables.

```bash
code=$(curl -s -o /dev/null -w '%{http_code}' -X PUT \
  --aws-sigv4 "aws:amz:us-east-1:s3" \
  --user "$AWS_ACCESS_KEY_ID:$AWS_SECRET_ACCESS_KEY" \
  "$S3/$bucket")
```

قبل التعديل ده، curl كان بيبعت الطلب من غير authentication فكان بيفشل لما SeaweedFS شغّل الـ s3.json. دلوقتي بيستخدم `--aws-sigv4` عشان يوقّع الطلب زي ما AWS S3 بيستناه.

---

### ملف 6: `docker-compose.yml` — "إعدادات البنية التحتية"

**المسار:** `docker-compose.yml`

> **في جملة:** عدّل إعدادات SeaweedFS عشان يستخدم ملف الهويات الجديد (s3.json).

```yaml
command: server -dir=/data -s3 -s3.port=8333 -s3.config=/etc/seaweedfs/s3.json
#                                              ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
#                          قبل كده مكانش فيه الجزء ده — يعني S3 كان مفتوح للكل
```

ضاف `-s3.config` عشان SeaweedFS يقرأ ملف الهويات.

```yaml
volumes:
  - s3data:/data
  - ./infra/s3/s3.json:/etc/seaweedfs/s3.json:ro    # ← ده الجديد
```

بيربط ملف `s3.json` من جهازك جوه الكونتينر. الـ `:ro` يعني read-only — الكونتينر يقدر يقرأه بس مش يعدّله.

---

## الجزء التالت: ملفات الإعدادات + الرسم

---

### ملفات الإعدادات (جملة واحدة لكل ملف):

- **`Directory.Packages.props`**: ضاف باكتين NuGet جداد — `MQTTnet 4.3.7` (مكتبة MQTT) و `StackExchange.Redis 2.8.41` (مكتبة Redis) — كـ central package versions عشان كل المشاريع تستخدم نفس النسخة.
- **`SmartCity.sln`**: ضاف مشروع `VehicleSimulator` في فولدر `tools` في الـ solution عشان Visual Studio يشوفه.
- **`VehicleSimulator.csproj`**: مشروع console app بسيط يعتمد على `MQTTnet` بس — مالوش أي ارتباط بباقي الخدمات.
- **`SmartCity.VehicleCommunication.csproj`**: ضاف references لـ `MQTTnet` و `StackExchange.Redis` عشان الكود الجديد يقدر يستخدمهم.

---

## الرسم بالأسهم: المسار الكامل

```
┌──────────────────┐
│ VehicleSimulator  │  (tools/VehicleSimulator/Program.cs)
│  "العربية الوهمية" │
└────────┬─────────┘
         │
         │  MQTT v5 (صورة + metadata على topic: vehicles/X/frames)
         │  MQTT    (JSON تيليمتري على topic: vehicles/X/telemetry)
         ▼
┌──────────────────┐
│   EMQX Broker    │  (MQTT broker — docker-compose)
│  "ساعي البريد"    │
└────────┬─────────┘
         │
         │  MQTT subscribe ($share/vehiclecomm/vehicles/+/frames)
         │  MQTT subscribe ($share/vehiclecomm/vehicles/+/telemetry)
         ▼
┌──────────────────────────────────────────┐
│        MqttSubscriberService             │
│  (VehicleCommunication service)          │
│  "الودان اللي بتسمع وبتوزّع"              │
│                                          │
│  1. بيستلم الرسالة من MQTT               │
│  2. بيتحقق من البيانات (validation)       │
│  3. لو صورة:                             │
│     ├─► يرفعها على S3 ──────────────►  SeaweedFS (frames-temp/)
│     └─► ينشر FrameReceived ────────►  RabbitMQ ──► [PR #2: AI Detection]
│  4. لو تيليمتري:                          │
│     ├─► ينشر VehicleTelemetryReceived ► RabbitMQ
│     └─► يحفظ آخر موقع ─────────────►  Redis (vehicle:pos:X)
└──────────────────────────────────────────┘
```

**المسار المختصر:**

```
عربية (Simulator) ──MQTT──► EMQX ──MQTT──► VehicleCommunication ─┬──S3──► صورة محفوظة
                                                                  ├──Bus──► FrameReceived إيفنت
                                                                  └──Redis──► آخر موقع
```

---

## ملخص PR #1 في 3 سطور

1. بنى **VehicleSimulator** — أداة بتعمل نفسها عربيات بتبعت صور و telemetry على MQTT.
2. بنى **MqttSubscriberService** — خدمة بتستلم الرسائل، بترفع الصور على S3، وبتنشر events على RabbitMQ.
3. ظبط **SeaweedFS** — ضاف authentication وربط ملف الهويات عشان الـ S3 يشتغل صح.

> ده الأساس اللي باقي الـ PRs بتتبني عليه — من غير الـ ingestion pipeline ده، مفيش صور هتدخل النظام أصلاً.
