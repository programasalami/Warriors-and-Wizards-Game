#region

using System.Collections.Specialized;
using System.Threading.Tasks;
using Common;
using Common.Database;
using Common.Utilities;

#endregion

namespace AccountServer.Systems.Account;

public class Register : RequestHandler {
    public override string Path => "/account/register";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        if (LoginGuards.RegisterByIp.IsBlocked(ip))
            return WriteError("Too many accounts created from this address recently. Please try again later.");

        var result = await DbClient.RegisterAsync(query["newUsername"], query["newPassword"], ip);
        if (result != RegisterStatus.Success)
            return WriteError(result.GetDescription());
        LoginGuards.RegisterByIp.Record(ip);
        return WriteSuccess();
    }
}