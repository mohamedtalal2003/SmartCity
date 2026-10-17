/// <summary>The command-line options of the simulator.</summary>
public sealed class SimOptions
{
    public int Vehicles { get; set; } = 5;
    public int IntervalMs { get; set; } = 2000;             // one photo per vehicle every N ms
    public int Frames { get; set; } = int.MaxValue;         // photos per vehicle, then wait until the queues are empty
    public string MqttHost { get; set; } = "localhost";
    public int MqttPort { get; set; } = 1883;
    public string GrpcUrl { get; set; } = "http://localhost:5211";

    // Hard cases
    public int OutsideIstanbulEvery { get; set; }           // every Nth photo is taken in Ankara (0 = never)
    public int ResendEvery { get; set; }                    // every Nth photo is sent twice, same frame id (0 = never)
    public int BlockedRetrySeconds { get; set; } = 60;      // after UNAUTHENTICATED / PERMISSION_DENIED

    public static SimOptions Parse(string[] args)
    {
        var o = new SimOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vehicles": o.Vehicles = int.Parse(args[++i]); break;
                case "--interval-ms": o.IntervalMs = int.Parse(args[++i]); break;
                case "--frames": o.Frames = int.Parse(args[++i]); break;
                case "--mqtt-host": o.MqttHost = args[++i]; break;
                case "--mqtt-port": o.MqttPort = int.Parse(args[++i]); break;
                case "--grpc-url": o.GrpcUrl = args[++i]; break;
                case "--outside-istanbul-every": o.OutsideIstanbulEvery = int.Parse(args[++i]); break;
                case "--resend-every": o.ResendEvery = int.Parse(args[++i]); break;
                case "--blocked-retry-s": o.BlockedRetrySeconds = int.Parse(args[++i]); break;
                default: throw new ArgumentException($"Unknown option {args[i]}");
            }
        }
        return o;
    }
}
