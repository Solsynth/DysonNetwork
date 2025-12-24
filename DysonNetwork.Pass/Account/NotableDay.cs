using Nager.Holiday;
using NodaTime;

namespace DysonNetwork.Pass.Account;

/// <summary>
/// Reference from Nager.Holiday
/// </summary>
public enum NotableHolidayType
{
    /// <summary>Public holiday</summary>
    Public,
    /// <summary>Bank holiday, banks and offices are closed</summary>
    Bank,
    /// <summary>School holiday, schools are closed</summary>
    School,
    /// <summary>Authorities are closed</summary>
    Authorities,
    /// <summary>Majority of people take a day off</summary>
    Optional,
    /// <summary>Optional festivity, no paid day off</summary>
    Observance,
}


public class NotableDay
{
    public Instant Date { get; set; }
    public string? LocalName { get; set; }
    public string? GlobalName { get; set; }
    public string? LocalizableKey { get; set; }
    public string? CountryCode { get; set; }
    public NotableHolidayType[] Holidays { get; set; } = [];

    public static NotableDay FromNagerHoliday(PublicHoliday holiday)
    {
        return new NotableDay()
        {
            Date = Instant.FromDateTimeUtc(holiday.Date.ToUniversalTime()),
            LocalName = holiday.LocalName,
            GlobalName = holiday.Name,
            CountryCode = holiday.CountryCode,
            Holidays = holiday.Types?.Select(x => x switch
            {
                PublicHolidayType.Public => NotableHolidayType.Public,
                PublicHolidayType.Bank => NotableHolidayType.Bank,
                PublicHolidayType.School => NotableHolidayType.School,
                PublicHolidayType.Authorities => NotableHolidayType.Authorities,
                PublicHolidayType.Optional => NotableHolidayType.Optional,
                _ => NotableHolidayType.Observance
            }).ToArray() ?? [],
        };
    }
}