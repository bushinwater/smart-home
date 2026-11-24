using System.Text.Json;

namespace SmartHome.Simulator;

public class Heating
{
    // the boiler turns on below (target - x) and off above (target + x)
    private const double Hysteresis = 0.5;

    public string Mode
    {
        get;
        private set;
    } = "manual";

    public double TargetTemperature { get; private set; } = 21.0;

    public bool BoilerOn { get; private set; }

    // works on /home/heating/set
    // retarns false and changes nothing if the command is invalid

    public bool Apply(string payload)
    {
        HeatingSetCommand? command;
        try
        {
            command = JsonSerializer.Deserialize<HeatingSetCommand>(payload, JsonSerializerOptions.Web);
        }
        catch(JsonException)
        {
            return false;
        }

        if (command is null) return false;
        if (command.TargetTemperature is < 5 or > 30) return false;
        if (command.Mode is not null && command.Mode is not ("off" or "manual")) return false;
        
        Mode = command.Mode ?? Mode;
        TargetTemperature = command.TargetTemperature ?? TargetTemperature;
        return true;
    }

    // Thermostat desicdes whether the boiler should run
    // returns true if the BoilerOn state changed, so the caller knows it should publish the new state
    public bool Update(double indoorTemperature)
    {
        var shouldHeat = BoilerOn;

        if (Mode == "off") shouldHeat = false;
        else if (indoorTemperature <= TargetTemperature - Hysteresis / 2) shouldHeat = true;
        else if (indoorTemperature >= TargetTemperature + Hysteresis / 2) shouldHeat = false;

        var changed = shouldHeat != BoilerOn;
        BoilerOn = shouldHeat;
        return changed;
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new { mode = Mode, targetTemperature = TargetTemperature, boilerOn = BoilerOn }); 

}

public record HeatingSetCommand(String? Mode, Double? TargetTemperature);