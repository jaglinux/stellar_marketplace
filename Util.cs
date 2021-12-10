using System;
using System.Linq;
using Stellmart.Api.Context.Entities;
using Stellmart.Api.Data.Horizon;

namespace Stellmart.Api.Services
{
    public static class Utils
    {
        private static long GetCurrentTimeInSeconds()
        {
            return (long) (DateTime.UtcNow - new DateTime(year: 1970, month: 1, day: 1)).TotalSeconds;
        }

        private static long ConvertDateToSeconds(DateTime date)
        {
            return (long) (date.ToUniversalTime() - new DateTime(year: 1970, month: 1, day: 1)).TotalSeconds;
        }

        public static bool VerifyTimeBound(DateTime minimum, DateTime maximum)
        {
            var noDelay = true;

            var currentTime = GetCurrentTimeInSeconds();

            if (minimum == default(DateTime) && maximum == default(DateTime))
                return noDelay;

            if (currentTime < ConvertDateToSeconds(minimum))
            {
                noDelay = false;
            }

            if (currentTime > ConvertDateToSeconds(maximum))
            {
                noDelay = false;
            }

            return noDelay;
        }
        public static int VerifySign(PreTransaction preTransaction, HorizonAccountWeightModel weights)
        {
            int weightSum = 0;
            //verify each signature
            var signerWeight = new HorizonAccountSignerModel();
            foreach (Signature signature in preTransaction.Signatures)
            {
                signerWeight = weights.Signer.FirstOrDefault(predicate: x => x.Signer == signature.PublicKey);
                if (signature.Signed == true && signerWeight != null)
                {
                    weightSum += signerWeight.Weight;
                }
            }
            return weightSum;
        }
    }
}
