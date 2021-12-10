using System.Collections.Generic;
using System.Threading.Tasks;
using stellar_dotnet_sdk;
using Stellmart.Api.Data.Enums;
using Stellmart.Api.Data.Horizon;
using Stellmart.Api.Services.Interfaces;

namespace Stellmart.Api.Services
{
    public class TokenService : ITokenService
    {
        private readonly IHorizonService _horizonService;

        public TokenService(IHorizonService horizonService)
        {
            _horizonService = horizonService;
        }

        public async Task<HorizonTokenModel> CreateAsset(string name, string limit)
        {
            var issuer = _horizonService.CreateAccount();
            var distributor = _horizonService.CreateAccount();

            var asset = new HorizonAssetModel {
                                                AssetCode = name, 
                                                AssetType = "credit_alphanum4",
                                                AssetIssuerPublicKey = issuer.PublicKey
                                              };

            var token = new HorizonTokenModel {
                                                HorizonAsset = asset,
                                                MaxCoinLimit = limit
                                              };

            //TBD : Real network code is pending
            //Fund minimum XLM to create operations
            if(_horizonService.IsTestNetwork()) {
                await _horizonService.FundTestAccountAsync(issuer.PublicKey);
                await _horizonService.FundTestAccountAsync(distributor.PublicKey);
            }

            token.IssuerAccount = issuer;

            token.Distributor = distributor;

            //Create trustline from Distributor to Issuer
            var operations = new List<Operation>();
            var trustModel = new HorizonTrustParameterModel {
                                    SourceAccountPublicKey = distributor.PublicKey,
                                    Limit = limit,
                                    AssetIssuerPublicKey = issuer.PublicKey,
                                    AssetCode = name,
                                    AssetType = "credit_alphanum4"
                                    };
            var trustOperation = _horizonService.ChangeTrustOperation(trustModel);
            operations.Add(trustOperation);

            var xdrTransaction = await _horizonService.CreateTransaction(distributor.PublicKey, operations, time: null, sequence: 0);

            var response = await _horizonService.SubmitTransaction(_horizonService.SignTransaction(distributor.SecretKey, xdrTransaction));
            if (response.IsSuccess() == false)
            {
                //Console.WriteLine("Failure: %s", response.ResultXdr);
                return null;
            }

            token.State = CustomTokenState.CREATE_CUSTOM_TOKEN;

            return token;
        }

        public async Task<bool> MoveAssetToDistributor(HorizonTokenModel token)
        {
            if (token.State != CustomTokenState.CREATE_CUSTOM_TOKEN)
            {
                return false;
            }

            token.MaxCoinLimit = token.MaxCoinLimit;

            var payment = new HorizonPaymentParameterModel
                        {
                            Amount = token.MaxCoinLimit,
                            AssetCode = token.HorizonAsset.AssetCode,
                            AssetIssuerPublicKey = token.IssuerAccount.PublicKey,
                            SourceAccountPublicKey = token.IssuerAccount.PublicKey,
                            DestinationAccountPublicKey = token.Distributor.PublicKey
                        };

            var result = await _horizonService.PaymentTransaction(payment, token.IssuerAccount.SecretKey);

            if (result)
            {
                token.State = CustomTokenState.MOVE_CUSTOM_TOKEN;
            }

            return result;
        }

        public async Task<bool> LockIssuer(HorizonTokenModel token)
        {
            if (token.State == CustomTokenState.MOVE_CUSTOM_TOKEN)
            {
                //Set threshold and weights of Issuer account as 0; so that no more coin can be minted.
                //All the coins should have been transferred to Distribution account by now.
                //Its the responsibility of the Distribution account to transfer the tokens to others.
                var weightParam = new HorizonAccountWeightParameterModel { MasterWeight = 0};

                //Let the SignerSecret be null
                var operations = new List<Operation>();

                var setOptionsWeightOperation = _horizonService.SetOptionsWeightOperation(token.IssuerAccount.PublicKey, weightParam);
                operations.Add(setOptionsWeightOperation);

                var xdrTransaction = await _horizonService.CreateTransaction(token.IssuerAccount.PublicKey, operations, time: null, sequence: 0);

                var response = await _horizonService.SubmitTransaction(_horizonService.SignTransaction(token.IssuerAccount.SecretKey, xdrTransaction));
                if (response.IsSuccess() == false)
                {
                    //Console.WriteLine("Failure: %s", response.ResultXdr);
                    return false;
                }

                token.State = CustomTokenState.LOCK_CUSTOM_TOKEN;

                return true;
            }

            return false;
        }
    }
}
