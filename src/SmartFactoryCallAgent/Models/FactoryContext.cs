namespace SmartFactoryCallAgent.Models;

public class FactoryContext
{
    public List<TelemetryRecord> TelemetryData { get; set; } = new();
    public List<AssemblyEventRecord> AssemblyEvents { get; set; } = new();
    public KpiSummaryRecord? KpiSummary { get; set; }
    public List<OrderRecord> ActiveOrders { get; set; } = new();
    public List<SupplyRiskRecord> SupplyRisks { get; set; } = new();
    public string TelemetryJson { get; set; } = string.Empty;
    public string AssemblyEventsJson { get; set; } = string.Empty;
    public string KpiJson { get; set; } = string.Empty;
    public string OrdersJson { get; set; } = string.Empty;
    public string SupplyRisksJson { get; set; } = string.Empty;
}

public class TelemetryRecord
{
    public string MachineId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public double Vibration { get; set; }
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class AssemblyEventRecord
{
    public string StationName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class KpiSummaryRecord
{
    public double AvgOee { get; set; }
    public double AvgScrapRate { get; set; }
    public double AvgUptime { get; set; }
    public int TotalUnitsProduced { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
}

public class OrderRecord
{
    public string OrderId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public string StationName { get; set; } = string.Empty;
}

public class SupplyRiskRecord
{
    public string Material { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string RiskDescription { get; set; } = string.Empty;
    public DateTimeOffset AssessmentDate { get; set; }
}
