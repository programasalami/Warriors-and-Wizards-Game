using System;
using Common.Database;

namespace AccountServer.Systems.Account;

// Per-IP limits for the two endpoints anyone on the internet can hit without an account (2026-09-21 audit, C7 / F43).
// Per-account-name limits for failed logins live in DbClient.LoginAttempts, so the game server's login path is covered too.
public static class LoginGuards {
    // Failed password checks from one address: 20 in 10 minutes, then that address waits.
    public static readonly AttemptLimiter VerifyByIp = new(20, TimeSpan.FromMinutes(10));

    // Successful registrations from one address: 5 per hour.
    public static readonly AttemptLimiter RegisterByIp = new(5, TimeSpan.FromHours(1));
}
