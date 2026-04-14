using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Microsoft.Extensions.Options;
using SmartFactoryCallAgent.Configuration;
using SmartFactoryCallAgent.Models;
using System.Text.Json;

namespace SmartFactoryCallAgent.Services;

public class FabricDataService
{
    private readonly FabricSettings _settings;
    private readonly ILogger<FabricDataService> _logger;

    public FabricDataService(IOptions<FabricSettings> settings, ILogger<FabricDataService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<FactoryContext> QueryContextAsync(string machineId, string stationName)
    {
        var context = new FactoryContext();

        try
        {
            using var client = CreateKustoClient();

            var telemetry = await QueryTelemetryAsync(client, machineId);
            var assemblyEvents = await QueryAssemblyEventsAsync(client, stationName);
            var kpis = await QueryKpisAsync(client);
            var orders = await QueryActiveOrdersAsync(client, stationName);
            var supplyRisks = await QuerySupplyRisksAsync(client);

            context.TelemetryData = telemetry;
            context.AssemblyEvents = assemblyEvents;
            context.KpiSummary = kpis;
            context.ActiveOrders = orders;
            context.SupplyRisks = supplyRisks;

            context.TelemetryJson = JsonSerializer.Serialize(telemetry);
            context.AssemblyEventsJson = JsonSerializer.Serialize(assemblyEvents);
            context.KpiJson = JsonSerializer.Serialize(kpis);
            context.OrdersJson = JsonSerializer.Serialize(orders);
            context.SupplyRisksJson = JsonSerializer.Serialize(supplyRisks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying Fabric Eventhouse for machine {MachineId}, station {StationName}",
                Sanitize(machineId), Sanitize(stationName));
        }

        return context;
    }

    public async Task<string> ExecuteRawQueryAsync(string kqlQuery)
    {
        try
        {
            using var client = CreateKustoClient();
            var results = new List<Dictionary<string, object?>>();

            using var reader = await client.ExecuteQueryAsync(
                _settings.DatabaseName,
                kqlQuery,
                new ClientRequestProperties());

            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            return JsonSerializer.Serialize(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing raw KQL query");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private ICslQueryProvider CreateKustoClient()
    {
        var kcsb = new KustoConnectionStringBuilder(_settings.KustoEndpoint, _settings.DatabaseName)
            .WithAadAzureTokenCredentialsAuthentication(new Azure.Identity.DefaultAzureCredential());
        return KustoClientFactory.CreateCslQueryProvider(kcsb);
    }

    private async Task<List<TelemetryRecord>> QueryTelemetryAsync(ICslQueryProvider client, string machineId)
    {
        // Use parameterized query to avoid KQL injection
        var query = $"""
            declare query_parameters(machineId:string);
            MachineTelemetry
            | where MachineId == machineId
            | where Timestamp > ago(15m)
            | order by Timestamp desc
            | take 100
            """;

        var props = new ClientRequestProperties();
        props.SetParameter("machineId", machineId);

        var results = new List<TelemetryRecord>();
        using var reader = await client.ExecuteQueryAsync(_settings.DatabaseName, query, props);
        while (reader.Read())
        {
            results.Add(new TelemetryRecord
            {
                MachineId = reader["MachineId"]?.ToString() ?? string.Empty,
                Timestamp = reader["Timestamp"] is DateTimeOffset dto ? dto : DateTimeOffset.UtcNow,
                Vibration = reader["Vibration"] is double v ? v : 0,
                Temperature = reader["Temperature"] is double t ? t : 0,
                Pressure = reader["Pressure"] is double p ? p : 0,
                Status = reader["Status"]?.ToString() ?? string.Empty
            });
        }
        return results;
    }

    private async Task<List<AssemblyEventRecord>> QueryAssemblyEventsAsync(ICslQueryProvider client, string stationName)
    {
        var query = $"""
            declare query_parameters(stationName:string);
            AssemblyEvents
            | where StationName == stationName
            | where Timestamp > ago(1h)
            | order by Timestamp desc
            | take 50
            """;

        var props = new ClientRequestProperties();
        props.SetParameter("stationName", stationName);

        var results = new List<AssemblyEventRecord>();
        using var reader = await client.ExecuteQueryAsync(_settings.DatabaseName, query, props);
        while (reader.Read())
        {
            results.Add(new AssemblyEventRecord
            {
                StationName = reader["StationName"]?.ToString() ?? string.Empty,
                EventType = reader["EventType"]?.ToString() ?? string.Empty,
                Timestamp = reader["Timestamp"] is DateTimeOffset dto ? dto : DateTimeOffset.UtcNow,
                OrderId = reader["OrderId"]?.ToString() ?? string.Empty,
                Details = reader["Details"]?.ToString() ?? string.Empty
            });
        }
        return results;
    }

    private async Task<KpiSummaryRecord?> QueryKpisAsync(ICslQueryProvider client)
    {
        var query = """
            ProductionKPIs
            | where Timestamp > ago(8h)
            | summarize
                AvgOee = avg(OEE),
                AvgScrapRate = avg(ScrapRate),
                AvgUptime = avg(Uptime),
                TotalUnitsProduced = sum(UnitsProduced),
                PeriodStart = min(Timestamp),
                PeriodEnd = max(Timestamp)
            """;

        using var reader = await client.ExecuteQueryAsync(_settings.DatabaseName, query, new ClientRequestProperties());
        if (reader.Read())
        {
            return new KpiSummaryRecord
            {
                AvgOee = reader["AvgOee"] is double oee ? oee : 0,
                AvgScrapRate = reader["AvgScrapRate"] is double sr ? sr : 0,
                AvgUptime = reader["AvgUptime"] is double ut ? ut : 0,
                TotalUnitsProduced = reader["TotalUnitsProduced"] is int tp ? tp : 0,
                PeriodStart = reader["PeriodStart"] is DateTimeOffset ps ? ps : DateTimeOffset.UtcNow,
                PeriodEnd = reader["PeriodEnd"] is DateTimeOffset pe ? pe : DateTimeOffset.UtcNow
            };
        }
        return null;
    }

    private async Task<List<OrderRecord>> QueryActiveOrdersAsync(ICslQueryProvider client, string stationName)
    {
        var query = $"""
            declare query_parameters(stationName:string);
            Orders
            | where Status != 'Completed'
            | where StationName == stationName or stationName == ''
            | order by DueDate asc
            | take 20
            """;

        var props = new ClientRequestProperties();
        props.SetParameter("stationName", stationName);

        var results = new List<OrderRecord>();
        using var reader = await client.ExecuteQueryAsync(_settings.DatabaseName, query, props);
        while (reader.Read())
        {
            results.Add(new OrderRecord
            {
                OrderId = reader["OrderId"]?.ToString() ?? string.Empty,
                ProductName = reader["ProductName"]?.ToString() ?? string.Empty,
                Status = reader["Status"]?.ToString() ?? string.Empty,
                Quantity = reader["Quantity"] is int q ? q : 0,
                DueDate = reader["DueDate"] is DateTimeOffset dd ? dd : DateTimeOffset.UtcNow,
                StationName = reader["StationName"]?.ToString() ?? string.Empty
            });
        }
        return results;
    }

    private async Task<List<SupplyRiskRecord>> QuerySupplyRisksAsync(ICslQueryProvider client)
    {
        var query = """
            SupplyRisk
            | where RiskLevel in ('High', 'Critical')
            | order by AssessmentDate desc
            | take 10
            """;

        var results = new List<SupplyRiskRecord>();
        using var reader = await client.ExecuteQueryAsync(_settings.DatabaseName, query, new ClientRequestProperties());
        while (reader.Read())
        {
            results.Add(new SupplyRiskRecord
            {
                Material = reader["Material"]?.ToString() ?? string.Empty,
                Supplier = reader["Supplier"]?.ToString() ?? string.Empty,
                RiskLevel = reader["RiskLevel"]?.ToString() ?? string.Empty,
                RiskDescription = reader["RiskDescription"]?.ToString() ?? string.Empty,
                AssessmentDate = reader["AssessmentDate"] is DateTimeOffset ad ? ad : DateTimeOffset.UtcNow
            });
        }
        return results;
    }

    private static string Sanitize(string value)
        => value.ReplaceLineEndings(" ").Replace("\t", " ");
}
