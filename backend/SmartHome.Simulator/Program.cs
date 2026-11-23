
// credentials line in user-secrets
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Protocol;

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var username = config["Mqtt:Username"] ?? throw new InvalidOperationException("Mqtt: Useername is not set in user-secrets");
var password = config["Mqtt:Password"] ?? throw new InvalidOperationException("Mqtt: Password is not set in user-secrets");

var factory = new MqttClientFactory();
using var client = factory.CreateMqttClient();

var statusTopic = "home/devices/esp32-main/status";

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

// ConnectAsync does not throw when the broker refuses us, so we must check the result
var result  = await client.ConnectAsync(options);
if (result.ResultCode != MqttClientConnectResultCode.Success)
{
    Console.WriteLine($"Broker refused the connection: {result.ResultCode}");
    return;
}
Console.WriteLine("Connected to MQTT broker");

var status = JsonSerializer.Serialize(new { online = true, firmware = "0.1.0" });
var statusMessage = new MqttApplicationMessageBuilder()
    .WithTopic(statusTopic)
    .WithPayload(status)
    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
    .WithRetainFlag()
    .Build();

await client.PublishAsync(statusMessage);
Console.WriteLine($"Published {statusTopic} {status}");

using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
do
{
    var state = new
    {
        temperature = Math.Round(21.5 + Random.Shared.NextDouble() * 10, 1),
        humidity = Math.Round(40 + Random.Shared.NextDouble() * 10, 1)
    };
    var payload = JsonSerializer.Serialize(state);

    var message = new MqttApplicationMessageBuilder()
        .WithTopic("home/sensors/indoor/state")
        .WithPayload(payload)
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
        .WithRetainFlag()
        .Build();

    await client.PublishAsync(message);
    Console.WriteLine($"Published home/sensors/indoor/state {payload}");
}
while (await timer.WaitForNextTickAsync());