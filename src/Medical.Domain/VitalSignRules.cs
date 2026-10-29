namespace Medical.Domain;

/// <summary>Clinical thresholds used to flag abnormal vital signs.</summary>
public static class VitalSignRules
{
    public const int HighSystolic = 140;
    public const int HighDiastolic = 90;
    public const int LowSystolic = 90;
    public const int LowDiastolic = 60;
    public const decimal HighGlucose = 180m;
    public const decimal LowGlucose = 70m;
    public const int LowOxygen = 92;
    public const int TachycardiaHeartRate = 100;
    public const decimal FeverTemperature = 38m;

    public static IReadOnlyList<string> ComputeFlags(
        int systolic, int diastolic, decimal glucose, int oxygenSaturation, int? heartRate, decimal? temperature)
    {
        var flags = new List<string>(4);
        if (systolic >= HighSystolic || diastolic >= HighDiastolic) flags.Add("HighBloodPressure");
        if (systolic < LowSystolic || diastolic < LowDiastolic) flags.Add("LowBloodPressure");
        if (glucose > HighGlucose) flags.Add("HighGlucose");
        if (glucose < LowGlucose) flags.Add("LowGlucose");
        if (oxygenSaturation < LowOxygen) flags.Add("LowOxygen");
        if (heartRate > TachycardiaHeartRate) flags.Add("Tachycardia");
        if (temperature >= FeverTemperature) flags.Add("Fever");
        return flags;
    }
}
