using System;

namespace WarpTen.Simulation
{
    public sealed class WarpEntryPolicy
    {
        private double nextAttempt;
        public string Failure { get; private set; }

        public bool TryEnter(DrivePhase phase, bool inWarp, bool canWarp, double now, Action enter, Func<bool> entered)
        {
            if (inWarp || phase != DrivePhase.Ready)
            {
                Failure = null;
                return false;
            }

            if (!canWarp || now < nextAttempt)
                return false;
            nextAttempt = now + 1;
            try
            {
                enter();
                if (!entered())
                {
                    Failure = "FTL entry failed: native space did not change";
                    return false;
                }

                Failure = null;
                return true;
            }
            catch (Exception error)
            {
                Failure = "FTL entry failed: " + error.Message;
                return false;
            }
        }
    }
}