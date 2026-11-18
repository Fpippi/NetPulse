namespace NetPulse.Core.Discovery;

/// <summary>
/// A time of day when a device tends to drop off, e.g. most absences starting between 20:00 and
/// 22:00: a hint at something on a schedule (a microwave, a neighbour's network, the router's own
/// channel scan or a power-saving setting).
/// </summary>
/// <param name="FromHour">Start of the two-hour window, local time (0–23).</param>
/// <param name="Count">Absences that started in the window.</param>
/// <param name="Total">All absences looked at.</param>
public sealed record AbsencePattern(int FromHour, int Count, int Total)
{
    /// <summary>Fewer absences than this say nothing about a time of day.</summary>
    public const int MinAbsences = 3;

    public const int WindowHours = 2;

    public int ToHour => (FromHour + WindowHours) % 24;

    public string Describe() =>
        $"{Count} of {Total} drops started between {FromHour:00}:00 and {ToHour:00}:00.";

    /// <summary>
    /// The two-hour window (any start hour, across midnight too) holding the most absence starts,
    /// if it holds at least <see cref="MinAbsences"/> and half of them. Null otherwise.
    /// </summary>
    public static AbsencePattern? Find(IEnumerable<DeviceAbsence> absences, TimeZoneInfo timeZone)
    {
        var perHour = new int[24];
        var total = 0;
        foreach (var absence in absences)
        {
            perHour[TimeZoneInfo.ConvertTime(absence.Start, timeZone).Hour]++;
            total++;
        }

        if (total < MinAbsences)
        {
            return null;
        }

        var best = Enumerable.Range(0, 24)
            .Select(hour => (Hour: hour, Count: Enumerable.Range(0, WindowHours).Sum(i => perHour[(hour + i) % 24])))
            .MaxBy(w => w.Count);

        return best.Count >= MinAbsences && best.Count * 2 >= total
            ? new AbsencePattern(best.Hour, best.Count, total)
            : null;
    }
}
