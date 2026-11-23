using System.Text.Json;

namespace SmartHome.Simulator;

public class Heating
{
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
        if (command is not null && command.Mode is not ("off" or "manual")) return false;   // tvoje
        if (command.Mode is not null && command.Mode is not ("off" or "manual")) return false;   // ispravno
        
        Mode = command.Mode ?? Mode;
        TargetTemperature = command.TargetTemperature ?? TargetTemperature;
        return true;
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new { mode = Mode, targetTemperature = TargetTemperature, boilerOn = BoilerOn }); 

}

public record HeatingSetCommand(String? Mode, Double? TargetTemperature);