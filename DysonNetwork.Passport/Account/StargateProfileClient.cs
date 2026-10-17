using Grpc.Core;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Passport.Account;

/// <summary>
/// A <see cref="DyProfileService.DyProfileServiceClient"/> dialed at Stargate
/// (<c>https://_grpc.stargate</c>).
///
/// <para>Passport still serves the whole <c>DyProfileService</c>, and the unkeyed
/// <c>DyProfileServiceClient</c> registration points back at Passport itself
/// (<c>https://_grpc.passport</c>) because profile, badge and board reads stay
/// local. The relationship RPCs, however, moved to Stargate along with the
/// <c>account_relationships</c> table, so they need their own client.</para>
///
/// <para>The fleet helper <c>AddGrpcClientWithSharedChannel</c> registers clients
/// by their CLR type, so registering a second plain
/// <c>DyProfileServiceClient</c> would overwrite the local one for every
/// existing consumer (including the auth handler). A distinct client type keeps
/// both registrations independent.</para>
/// </summary>
public sealed class StargateProfileClient(CallInvoker callInvoker)
    : DyProfileService.DyProfileServiceClient(callInvoker)
{
}