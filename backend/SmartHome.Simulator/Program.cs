using System.Diagnostics;
// credentials line in user-secrets
using System.Text.Json;
using SmartHome.Simulator;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Protocol;

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var username = config["Mqtt:Username"] ?? throw new InvalidOperationException("Mqtt: Username is not set in user-secrets");
var password = config["Mqtt:Password"] ?? throw new InvalidOperationException("Mqtt: Password is not set in user-secrets");

var factory = new MqttClientFactory();
using var client = factory.CreateMqttClient();

var statusTopic = "home/devices/esp32-main/status";
var heating = new Heating();
var room = new Room();
// last value the "sensor" measured; the thermostat and the command handler both use it
var lastIndoorTemperature = room.Temperature;

var options = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", 1883)
    .WithClientId("esp32-main")
    .WithCredentials(username, password)
    .WithCleanStart() // broker must not queue commands while we are offline
    // last will
    .WithWillTopic(statusTopic)
    .WithWillPayload(JsonSerializer.Serialize(new {online = false}))
    .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
    .WithWillRetain()
    .Build();

// Called by MQTTNet for every msg that arrives on a topic we subscribed
client.ApplicationMessageReceivedAsync += async e =>
{
    var topic = e.ApplicationMessage.Topic;
    var payload = e.ApplicationMessage.ConvertPayloadToString();
    Console.WriteLine($"Received {topic} {payload}");

    if (topic == "home/heating/set")
    {
        if (!heating.Apply(payload)) Console.WriteLine("Invalid heating command, ignored!");

        // react to the target right away
        heating.Update(lastIndoorTemperature);

        await PublishStateAsync("home/heating/state", heating.ToJson(), MqttQualityOfServiceLevel.AtLeastOnce);
    }
};

// ConnectAsync does not throw when the broker refuses us, so we must check the result
var result  = await client.ConnectAsync(options);
if (result.ResultCode != MqttClientConnectResultCode.Success)
{
    Console.WriteLine($"Broker refused the connection: {result.ResultCode}");
    return;
}
Console.WriteLine("Connected to MQTT broker");

await client.SubscribeAsync("home/heating/set", MqttQualityOfServiceLevel.AtLeastOnce);
Console.WriteLine("Subscribed to home/heating/set");

var status = JsonSerializer.Serialize(new { online = true, firmware = "0.1.0" });
await PublishStateAsync(statusTopic, status,  MqttQualityOfServiceLevel.AtLeastOnce);
await PublishStateAsync("home/heating/state", heating.ToJson(), MqttQualityOfServiceLevel.AtLeastOnce);

var clock = Stopwatch.StartNew();
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
do
{
    // forward by the real time that passed
    room.Update(clock.Elapsed, heating.BoilerOn);
    clock.Restart();

    // real sensor is never exact
    lastIndoorTemperature = Math.Round(room.Temperature + (Random.Shared.NextDouble() - 0.5) * 0.2, 1);

    var state = new
    {
        temperature = lastIndoorTemperature,
        humidity = Math.Round(40 + Random.Shared.NextDouble() * 10, 1)
    };
    await PublishStateAsync("home/sensors/indoor/state", JsonSerializer.Serialize(state), MqttQualityOfServiceLevel.AtMostOnce);

    // theromstat runs on the ESP32; publihs only when the boiler acutaly switches
    if (heating.Update(lastIndoorTemperature))
        await PublishStateAsync("home/heating/state", heating.ToJson(), MqttQualityOfServiceLevel.AtLeastOnce);
}
while (await timer.WaitForNextTickAsync());

async Task PublishStateAsync(string topic, string payload, MqttQualityOfServiceLevel qos)
{
    var message = new MqttApplicationMessageBuilder()
        .WithTopic(topic)
        .WithPayload(payload)
        .WithQualityOfServiceLevel(qos)
        .WithRetainFlag()
        .Build();

    await client.PublishAsync(message);
    Console.WriteLine($"Published {topic} {payload}");
}