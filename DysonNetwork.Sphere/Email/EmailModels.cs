namespace DysonNetwork.Sphere.Email;

public class LandingEmailModel
{
    public required string Name { get; set; }
    public required string Link { get; set; }  
}

public class AccountDeletionEmailModel
{
    public required string Name { get; set; }
    public required string Link { get; set; }
}

public class PasswordResetEmailModel
{
    public required string Name { get; set; }
    public required string Link { get; set; } 
}