using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Account;

/// <summary>
/// Service for handling username generation and validation
/// </summary>
public class AccountUsernameService(AppDatabase db)
{
    private readonly Random _random = new();

    /// <summary>
    /// Generates a unique username based on the provided base name
    /// </summary>
    /// <param name="baseName">The preferred username</param>
    /// <returns>A unique username</returns>
    public async Task<string> GenerateUniqueUsernameAsync(string baseName)
    {
        // Sanitize the base name
        var sanitized = SanitizeUsername(baseName);

        // If the base name is empty after sanitization, use a default
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "user";
        }

        // Check if the sanitized name is available
        if (!await IsUsernameExistsAsync(sanitized))
        {
            return sanitized;
        }

        // Try up to 10 times with random numbers
        for (int i = 0; i < 10; i++)
        {
            var suffix = _random.Next(1000, 9999);
            var candidate = $"{sanitized}{suffix}";

            if (!await IsUsernameExistsAsync(candidate))
            {
                return candidate;
            }
        }

        // If all attempts fail, use a timestamp
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{sanitized}{timestamp}";
    }

    /// <summary>
    /// Sanitizes a username by removing invalid characters and converting to lowercase
    /// </summary>
    public string SanitizeUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
            return string.Empty;

        // Replace spaces and special characters with underscores
        var sanitized = Regex.Replace(username, @"[^a-zA-Z0-9_\-]", "");

        // Convert to lowercase
        sanitized = sanitized.ToLowerInvariant();

        // Ensure it starts with a letter
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]))
        {
            sanitized = "u" + sanitized;
        }

        // Truncate if too long
        if (sanitized.Length > 30)
        {
            sanitized = sanitized[..30];
        }

        return sanitized;
    }

    /// <summary>
    /// Checks if a username already exists
    /// </summary>
    public async Task<bool> IsUsernameExistsAsync(string username)
    {
        return await db.Accounts.AnyAsync(a => a.Name == username);
    }

    /// <summary>
    /// Generates a username from an email address
    /// </summary>
    /// <param name="email">The email address to generate a username from</param>
    /// <returns>A unique username derived from the email</returns>
    public async Task<string> GenerateUsernameFromEmailAsync(string email)
    {
        if (string.IsNullOrEmpty(email))
            return await GenerateUniqueUsernameAsync("user");

        // Extract the local part of the email (before the @)
        var localPart = email.Split('@')[0];

        // Use the local part as the base for username generation
        return await GenerateUniqueUsernameAsync(localPart);
    }
}