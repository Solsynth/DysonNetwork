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

public class VerificationEmailModel
{
    public required string Name { get; set; }
    public required string Code { get; set; }
}

public class ContactVerificationEmailModel
{
    public required string Name { get; set; }
    public required string Link { get; set; }
}