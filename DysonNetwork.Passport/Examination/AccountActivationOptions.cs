namespace DysonNetwork.Passport.Examination;

public class AccountActivationOptions
{
    public bool RequireVerifiedContact { get; set; } = true;
    public List<string> RequiredTestKeys { get; set; } = [];
}
