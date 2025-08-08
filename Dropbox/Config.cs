using ECommons.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dropbox
{
    public class Config : IEzConfig
    {
        public int Delay = 1000;
        public bool NoOp = false;
        public bool AutoConfirm5 = false;
        public int AutoConfirmGil = 0;
        public bool Silent = false;
        public bool PermanentActive = false;
        public bool Active = false;
        public List<TradeQueueEntry> TradeQueue = [];
        public int TradeDelay = 12;
        public int TradeThrottle = 3000;
        public bool Busy = false;
        public bool WhitelistMode = false;
        public Dictionary<ulong, string> WhitelistedAccounts = [];
        public Dictionary<ulong, string> WhitelistedCharacters = [];
        public bool AutoClear = false;
    }
}
