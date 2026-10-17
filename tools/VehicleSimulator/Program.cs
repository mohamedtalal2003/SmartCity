// Simulated municipality vehicles that behave like the real Pi: frames over gRPC with a local queue (the "SD card"),
// small messages over MQTT. See docs/design/vehicle/vehicle-protocol.md.
//
//   dotnet run --project tools/VehicleSimulator -- --vehicles 2
//   options: --interval-ms 2000 --frames N --grpc-url http://localhost:5211 --mqtt-host localhost --mqtt-port 1883
//            --outside-istanbul-every N --resend-every N --blocked-retry-s 60

var options = SimOptions.Parse(args);
Console.WriteLine($"Starting simulator: {options.Vehicles} vehicles, a photo every {options.IntervalMs} ms, " +
    $"{(options.Frames == int.MaxValue ? "until Ctrl+C" : $"{options.Frames} photos each")}, gRPC {options.GrpcUrl}, " +
    $"MQTT {options.MqttHost}:{options.MqttPort}");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var stats = new Stats();
var vehicles = Enumerable.Range(1, options.Vehicles).Select(n => new SimVehicle(n, options, stats)).ToList();
var run = Task.WhenAll(vehicles.Select(v => v.RunAsync(cts.Token)));

// One summary line every 10 s: the replies since the last line, the frames still on the cards, the fleet state.
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
while (!run.IsCompleted)
{
    await Task.WhenAny(run, timer.WaitForNextTickAsync().AsTask());
    if (run.IsCompleted)
        break;

    var cards = string.Join(" ", vehicles.Select(v => $"{v.Id}={v.Pending}"));
    var fleet = vehicles.Any(v => v.Paused) ? "paused" : "running";
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SUMMARY last 10 s: {stats.TakeRecent()} | on cards: {vehicles.Sum(v => v.Pending)} ({cards}) | fleet: {fleet}");
}

await run;
Console.WriteLine($"Simulator stopped. Totals: {stats.Totals()} | left on cards: {vehicles.Sum(v => v.Pending)}");
