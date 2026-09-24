using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public static class ChineseHolidaysSeed
{
    // Priority: 1 = statutory holiday (days off), 2 = major traditional festival, 3 = minor observance/event.
    // Meta["calendar"] = "lunar" marks RecurrencePattern as a lunar calendar month-day.
    public static List<SnNotableDay> GetChineseHolidays()
    {
        return
        [
            // 春节 (Spring Festival / Chinese New Year) - 7 days
            new()
            {
                Name = "Spring Festival",
                LocalName = "春节",
                LocalizableKey = "SpringFestival",
                Description = "Chinese New Year, the most important traditional festival in China",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 2, 17, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday, NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["calendar"] = "lunar", ["priority"] = 1 },
                IsRecurring = true,
                RecurrencePattern = "01-01", // Lunar calendar
                IsPeriod = true,
                DisplayOrder = 1,
            },

            // 清明节 (Qingming Festival / Tomb Sweeping Day) - 3 days
            new()
            {
                Name = "Qingming Festival",
                LocalName = "清明节",
                LocalizableKey = "QingmingFestival",
                Description = "Traditional festival for honoring ancestors and spring outings",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 4, 4, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 4, 7, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday, NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["priority"] = 1 },
                IsRecurring = true,
                RecurrencePattern = "04-04", // Approximate solar date
                IsPeriod = true,
                DisplayOrder = 2,
            },

            // 劳动节 (Labour Day) - 5 days
            new()
            {
                Name = "Labour Day",
                LocalName = "劳动节",
                LocalizableKey = "LabourDay",
                Description = "International Workers' Day holiday",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday],
                Meta = new Dictionary<string, object> { ["priority"] = 1 },
                IsRecurring = true,
                RecurrencePattern = "05-01",
                IsPeriod = true,
                DisplayOrder = 3,
            },

            // 端午节 (Dragon Boat Festival) - 3 days
            new()
            {
                Name = "Dragon Boat Festival",
                LocalName = "端午节",
                LocalizableKey = "DragonBoatFestival",
                Description = "Traditional festival with dragon boat races and zongzi",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 6, 8, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 6, 11, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday, NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["calendar"] = "lunar", ["priority"] = 1 },
                IsRecurring = true,
                RecurrencePattern = "05-05", // Lunar calendar
                IsPeriod = true,
                DisplayOrder = 4,
            },

            // 中秋节 (Mid-Autumn Festival) - 3 days
            new()
            {
                Name = "Mid-Autumn Festival",
                LocalName = "中秋节",
                LocalizableKey = "MidAutumnFestival",
                Description = "Traditional festival for moon gazing and eating mooncakes",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 9, 15, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 9, 18, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday, NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["calendar"] = "lunar", ["priority"] = 1 },
                IsRecurring = true,
                RecurrencePattern = "08-15", // Lunar calendar
                IsPeriod = true,
                DisplayOrder = 5,
            },

            // 国庆节 (National Day) - 7 days
            new()
            {
                Name = "National Day",
                LocalName = "国庆节",
                LocalizableKey = "NationalDay",
                Description = "Celebration of the founding of the People's Republic of China",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 10, 8, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday],
                Meta = new Dictionary<string, object> { ["priority"] = 1 },
                IsRecurring = true,
                RecurrencePattern = "10-01",
                IsPeriod = true,
                DisplayOrder = 6,
            },

            // 元旦 (New Year's Day) - 3 days
            new()
            {
                Name = "New Year's Day",
                LocalName = "元旦",
                LocalizableKey = "NewYear",
                Description = "New Year's Day holiday",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 1, 4, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday],
                Meta = new Dictionary<string, object> { ["priority"] = 1 },
                IsRecurring = true,
                RecurrencePattern = "01-01",
                IsPeriod = true,
                DisplayOrder = 7,
            },

            // 除夕 (Lunar New Year's Eve) - 1 day, major traditional festival
            new()
            {
                Name = "Lunar New Year's Eve",
                LocalName = "除夕",
                LocalizableKey = "LunarNewYearEve",
                Description = "The evening before Chinese New Year, family reunion dinner",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 2, 9, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["calendar"] = "lunar", ["priority"] = 2 },
                IsRecurring = true,
                RecurrencePattern = "12-30", // Lunar calendar (last day of lunar year, 29 or 30)
                IsPeriod = false,
                DisplayOrder = 8,
            },

            // 元宵节 (Lantern Festival) - 1 day, major traditional festival
            new()
            {
                Name = "Lantern Festival",
                LocalName = "元宵节",
                LocalizableKey = "LanternFestival",
                Description = "End of Chinese New Year celebrations, lanterns and tangyuan",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 2, 25, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["calendar"] = "lunar", ["priority"] = 2 },
                IsRecurring = true,
                RecurrencePattern = "01-15", // Lunar calendar
                IsPeriod = false,
                DisplayOrder = 9,
            },

            // 植树节 (Arbor Day / Tree Planting Day)
            new()
            {
                Name = "Arbor Day",
                LocalName = "植树节",
                LocalizableKey = "ArborDay",
                Description = "National Tree Planting Day",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 3, 13, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Event],
                Meta = new Dictionary<string, object> { ["priority"] = 3 },
                IsRecurring = true,
                RecurrencePattern = "03-12",
                IsPeriod = false,
                DisplayOrder = 10,
            },

            // 五四青年节 (Youth Day)
            new()
            {
                Name = "Youth Day",
                LocalName = "五四青年节",
                LocalizableKey = "YouthDay",
                Description = "Commemoration of the May Fourth Movement",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 5, 4, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 5, 5, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Event, NotableDayTag.Memorial],
                Meta = new Dictionary<string, object> { ["priority"] = 3 },
                IsRecurring = true,
                RecurrencePattern = "05-04",
                IsPeriod = false,
                DisplayOrder = 11,
            },

            // 儿童节 (Children's Day)
            new()
            {
                Name = "Children's Day",
                LocalName = "儿童节",
                LocalizableKey = "ChildrenDay",
                Description = "International Children's Day",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 6, 2, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Event],
                Meta = new Dictionary<string, object> { ["priority"] = 3 },
                IsRecurring = true,
                RecurrencePattern = "06-01",
                IsPeriod = false,
                DisplayOrder = 12,
            },

            // 教师节 (Teachers' Day)
            new()
            {
                Name = "Teachers' Day",
                LocalName = "教师节",
                LocalizableKey = "TeachersDay",
                Description = "A day to honor teachers",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 9, 10, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 9, 11, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Event],
                Meta = new Dictionary<string, object> { ["priority"] = 3 },
                IsRecurring = true,
                RecurrencePattern = "09-10",
                IsPeriod = false,
                DisplayOrder = 13,
            },

            // 七夕 (Qixi Festival / Chinese Valentine's Day)
            new()
            {
                Name = "Qixi Festival",
                LocalName = "七夕节",
                LocalizableKey = "QixiFestival",
                Description = "Chinese Valentine's Day, the Cowherd and Weaver Girl festival",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 8, 10, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 8, 11, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["calendar"] = "lunar", ["priority"] = 2 },
                IsRecurring = true,
                RecurrencePattern = "07-07", // Lunar calendar
                IsPeriod = false,
                DisplayOrder = 14,
            },

            // 重阳节 (Double Ninth Festival)
            new()
            {
                Name = "Double Ninth Festival",
                LocalName = "重阳节",
                LocalizableKey = "DoubleNinthFestival",
                Description = "Traditional festival for respecting the elderly",
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 10, 11, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 10, 12, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Festival],
                Meta = new Dictionary<string, object> { ["calendar"] = "lunar", ["priority"] = 2 },
                IsRecurring = true,
                RecurrencePattern = "09-09", // Lunar calendar
                IsPeriod = false,
                DisplayOrder = 15,
            },
        ];
    }
}
