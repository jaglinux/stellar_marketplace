using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Stellmart.Api.Data.Horizon;
using Stellmart.Api.Data.Settings;
using Stellmart.Api.Services.Interfaces;

namespace Stellmart.Api.Services
{
    public class RewardService : IRewardService
    {
        private readonly IHorizonService _horizonService;
        private readonly HorizonKeyPairModel _worldSquareAccount;
        private readonly HorizonAssetModel _rewardAssetModel;
        private  int _maximumReward;

        public RewardService(IHorizonService horizonService,
                             IOptions<SignatureSettings> settings)
        {
            _horizonService = horizonService;
            _worldSquareAccount = new HorizonKeyPairModel {PublicKey = settings.Value.MasterPublicKey, SecretKey = settings.Value.MasterSecretKey};
            //ToDo : Get the assetmodel from settings
            _rewardAssetModel = new HorizonAssetModel
            {
                AssetType = "credit_alphanum4",
                AssetCode = "sqr",
                AssetIssuerPublicKey = "GAGYAIWVYD5VYEFPD44HCA3CVENC7LNA3X6OW4UAKIJNMVDBNBZM6I5AS"
            };
            // ToDo : obtain the maximum reward from settings
            _maximumReward = 5;
        }
        public async Task<int> ApplyRewardAsync(string buyerPublicKey)
        {
            var balanceModel = new HorizonBalanceParameterModel
            {
                AssetType = _rewardAssetModel.AssetType,
                AssetCode = _rewardAssetModel.AssetCode,
                AssetIssuerPublicKey = _rewardAssetModel.AssetIssuerPublicKey,
                SourceAccountPublicKey = buyerPublicKey
            };
            var rewardBalanceList = await _horizonService.GetAccountBalanceAsync(balanceModel);
            var rewardBalance = rewardBalanceList.FirstOrDefault();

            int.TryParse(rewardBalance.Balance, out var balanceInt);

            if(balanceInt > 0) {
                return Convert.ToInt32(Math.Min(Math.Log10(balanceInt / 10), _maximumReward));
            }
            else {
                return 0;
            }
        }
        public async Task<bool> GetRewardAsync(string buyerPublicKey, string rewardAmount)
        {
            var balanceModel = new HorizonBalanceParameterModel
            {
                AssetType = _rewardAssetModel.AssetType,
                AssetCode = _rewardAssetModel.AssetCode,
                AssetIssuerPublicKey = _rewardAssetModel.AssetIssuerPublicKey,
                SourceAccountPublicKey = _worldSquareAccount.PublicKey
            };

            var rewardBalanceList = await _horizonService.GetAccountBalanceAsync(balanceModel);
            var rewardBalance = rewardBalanceList.FirstOrDefault();

            int.TryParse(rewardBalance.Limit, out var limit);
            int.TryParse(rewardAmount, out var reward);
            int.TryParse(rewardBalance.Balance, out var balanceInt);


            if((reward + balanceInt) > limit) {
            var paymentModel = new HorizonPaymentParameterModel
            {
                AssetType = _rewardAssetModel.AssetType,
                AssetCode = _rewardAssetModel.AssetCode,
                AssetIssuerPublicKey = _rewardAssetModel.AssetIssuerPublicKey,
                SourceAccountPublicKey = _worldSquareAccount.PublicKey,
                DestinationAccountPublicKey = buyerPublicKey,
                Amount = rewardAmount
            };

                return await _horizonService.PaymentTransaction(paymentModel, _worldSquareAccount.SecretKey);
            }

            Console.WriteLine(value: "Buyer account has less Reward Trust");
            return false;
        }
    }
}
