namespace DysonNetwork.Pass.Account.Presences;

public enum PresenceUpdateStage
{
    /// <summary>
    /// Active users - online and have active presence activities
    /// </summary>
    Active,

    /// <summary>
    /// Maybe active users - online but no active presence activities
    /// </summary>
    Maybe,

    /// <summary>
    /// Cold users - offline users
    /// </summary>
    Cold
}
