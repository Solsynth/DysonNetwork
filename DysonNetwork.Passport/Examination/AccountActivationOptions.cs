namespace DysonNetwork.Passport.Examination;

public class AccountActivationOptions
{
    public bool TestsEnabled { get; set; } = true;
    public bool RequireVerifiedContact { get; set; } = true;
    public List<string> RequiredTestKeys { get; set; } = [];
}
