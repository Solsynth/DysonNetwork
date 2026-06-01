using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public static class ChineseHolidaysSeed
{
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
                StartDate = Instant.FromDateTimeUtc(new DateTime(2024, 1, 28, 0, 0, 0, DateTimeKind.Utc)),
                EndDate = Instant.FromDateTimeUtc(new DateTime(2024, 2, 4, 0, 0, 0, DateTimeKind.Utc)),
                Region = "CN",
                Tags = [NotableDayTag.Holiday, NotableDayTag.Festival],
                IsRecurring = true,
                RecurrencePattern = "01-01", // Lunar calendar
                IsPeriod = true,
                HolidayDays = ["01-28", "01-29", "01-30", "01-31", "02-01", "02-02", "02-03"],
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
                IsRecurring = true,
                RecurrencePattern = "04-04", // Approximate solar date
                IsPeriod = true,
                HolidayDays = ["04-04", "04-05", "04-06"],
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
                IsRecurring = true,
                RecurrencePattern = "05-01",
                IsPeriod = true,
                HolidayDays = ["05-01", "05-02", "05-03", "05-04", "05-05"],
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
                IsRecurring = true,
                RecurrencePattern = "05-05", // Lunar calendar
                IsPeriod = true,
                HolidayDays = ["06-08", "06-09", "06-10"],
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
                IsRecurring = true,
                RecurrencePattern = "08-15", // Lunar calendar
                IsPeriod = true,
                HolidayDays = ["09-15", "09-16", "09-17"],
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
                IsRecurring = true,
                RecurrencePattern = "10-01",
                IsPeriod = true,
                HolidayDays = ["10-01", "10-02", "10-03", "10-04", "10-05", "10-06", "10-07"],
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
                IsRecurring = true,
                RecurrencePattern = "01-01",
                IsPeriod = true,
                HolidayDays = ["01-01", "01-02", "01-03"],
                DisplayOrder = 7,
            },

            // Non-holiday events below

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
                IsRecurring = true,
                RecurrencePattern = "09-09", // Lunar calendar
                IsPeriod = false,
                DisplayOrder = 15,
            },
        ];
    }
}
