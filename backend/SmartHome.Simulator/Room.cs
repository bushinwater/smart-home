namespace SmartHome.Simulator;

// simple model of room: warms up while boiler runs, and loses heat towards the outside
public class Room
{
    private const double HeatingPerMinute = 0.5; // 0.5 celsius per minute while boiler is on
    private const double HeatingLossPerMinute = 0.02; // 2% goes toward outside from inside

    public double Temperature { get; private set; } = 20.0;
    public double OutdoorTemperature { get; } = 5.0;

    public void Update (TimeSpan elapsed, bool boilerOn)
    {
        var minutes = elapsed.TotalMinutes;

        if (boilerOn)
        {
            Temperature += HeatingPerMinute * minutes;
        }

        // the warmer the room is compered to outside, the faster it loses heat
        Temperature -= (Temperature - OutdoorTemperature) * HeatLossPerMinute * minutes;
    }
}