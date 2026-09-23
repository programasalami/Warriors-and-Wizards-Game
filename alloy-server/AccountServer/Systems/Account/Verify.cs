#region

using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Common;
using Common.Database;
using Common.Utilities;

#endregion

namespace AccountServer.Systems.Account;

public class Verify : RequestHandler {
    public override string Path => "/account/verify";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        if (LoginGuards.VerifyByIp.IsBlocked(ip))
            return WriteError(VerifyStatus.TooManyAttempts.GetDescription());

        var verify = await DbClient.VerifyAccount(query["username"], query["password"], Guid.Empty);
        var acc = verify.Acc;
        var status = verify.Status;
        if (acc == null) {
            if (status == VerifyStatus.InvalidCredentials)
                LoginGuards.VerifyByIp.Record(ip);
            return WriteError(status.GetDescription());
        }
        return acc.ToXml().ToString();
    }
}