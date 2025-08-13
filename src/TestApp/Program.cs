using Hangfire.Storage.MySql;

const string connectionString = "Server=devubu;Database=hf1;Uid=hf1;Pwd=sporak";

var s = new MySqlStorage(connectionString, new MySqlStorageOptions(), MySqlConnector.MySqlConnectorFactory.Instance);

var monitor = s.GetMonitoringApi();
var stats = monitor.GetStatistics();

Console.WriteLine("Ende");