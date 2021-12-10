using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Options;
using stellar_dotnet_sdk;
using stellar_dotnet_sdk.responses;
using stellar_dotnet_sdk.xdr;
using Stellmart.Api.Business.Managers.Interfaces;
using Stellmart.Api.Data.Horizon;
using Stellmart.Api.Data.Settings;
using Stellmart.Api.Services.Interfaces;
using Asset = stellar_dotnet_sdk.Asset;
using Operation = stellar_dotnet_sdk.Operation;
using Signer = stellar_dotnet_sdk.Signer;
using ResponseSigner = stellar_dotnet_sdk.responses.Signer;
using TimeBounds = stellar_dotnet_sdk.TimeBounds;
using Transaction = stellar_dotnet_sdk.Transaction;

namespace Stellmart.Api.Services
{
    public class HorizonService : IHorizonService
    {
        private readonly IHorizonServerManager _horizonServerManager;
        private readonly IOptions<HorizonSettings> _horizonSettings;
        private readonly IMapper _mapper;
        private readonly bool _testNetwork = false;
        private readonly List<HorizonBalanceParameterModel> _standardTokens;

        public HorizonService(IOptions<HorizonSettings> horizonSettings, IMapper mapper, IHorizonServerManager horizonServerManager)
        {
            _horizonSettings = horizonSettings;
            _mapper = mapper;
            _horizonServerManager = horizonServerManager;

            if (_horizonSettings.Value.Server.Contains(value: "testnet"))
            {
                Network.UseTestNetwork();
                _testNetwork = true;
            }
            else
            {
                Network.UsePublicNetwork();
            }

            _standardTokens = new List<HorizonBalanceParameterModel>();

            //ToDo : Get the asset code and public kets from settings
            _standardTokens.Add(new HorizonBalanceParameterModel {
                AssetCode = "sqr",
                AssetIssuerPublicKey = "GAGYAIWVYD5VYEFPD44HCA3CVENC7LNA3X6OW4UAKIJNMVDBNBZM6I5A"
            });

            _standardTokens.Add(new HorizonBalanceParameterModel {
                AssetCode = "wsd",
                AssetIssuerPublicKey = "GC6DDR4C3RSUEJRPU6FXWGPOW6DLXZF3UKNCBWG7X43ESMVABSIGJYNG"
            });

            _standardTokens.Add(new HorizonBalanceParameterModel {
                AssetCode = "usdr",
                AssetIssuerPublicKey = "GDF7UO7GHK4OYFY6U6QFTMGN47JFEJE54A54CMPFFOTNQEPDLCXGIKWL"
            });
        }
        public bool IsTestNetwork()
        {
;           return _testNetwork;
        }

        public HorizonKeyPairModel CreateAccount()
        {
            return _horizonServerManager.CreateAccount();
        }

        public async Task<HorizonFundTestAccountModel> FundTestAccountAsync(string publicKey)
        {
            if(_testNetwork)
            {
                // fund test acc
                await _horizonServerManager.FundTestAccountAsync(publicKey);

                //See our newly created account.
                var accountResponse = await _horizonServerManager.GetAccountAsync(publicKey);

                return _mapper.Map<HorizonFundTestAccountModel>(accountResponse);
            }
            return null;
        }

        public async Task<long> GetSequenceNumberAsync(string publicKey)
        {
            var accountResponse = await _horizonServerManager.GetAccountAsync(publicKey);

            return accountResponse.SequenceNumber;
        }

        public async Task<HorizonAccountWeightModel> GetAccountWeightAsync(string publicKey)
        {
            var accountResponse = await _horizonServerManager.GetAccountAsync(publicKey);
            var weight = new HorizonAccountWeightModel
            {
                LowThreshold = accountResponse.Thresholds.LowThreshold,
                MediumThreshold = accountResponse.Thresholds.MedThreshold,
                HighThreshold = accountResponse.Thresholds.HighThreshold,
                Signer = new List<HorizonAccountSignerModel>()
            };

            foreach (ResponseSigner signer in accountResponse.Signers) {
                weight.Signer.Add(new HorizonAccountSignerModel {
                    Signer = signer.Key,
                    Weight = signer.Weight
                });
            }
            return weight;
        }

        public async Task<List<HorizonBalanceModel>> GetAccountBalanceAsync(HorizonBalanceParameterModel model)
        {
            var accountResponse = await _horizonServerManager.GetAccountAsync(model.SourceAccountPublicKey);
            Balance balance;
            var horizonBalance = new List<HorizonBalanceModel>();

            if(model.StandardTokens == false) {
                if (model.AssetCode != null)
                {
                    balance = accountResponse.Balances.FirstOrDefault(predicate: x => x.AssetCode == model.AssetCode && x.AssetIssuer == model.AssetIssuerPublicKey);
                }
                else
                {
                    balance = accountResponse.Balances.FirstOrDefault(predicate: x => x.AssetType == "native");
                }
                horizonBalance.Add((balance != null)?_mapper.Map<HorizonBalanceModel>(balance):null);
            } else {
                balance = accountResponse.Balances.FirstOrDefault(predicate: x => x.AssetType == "native");

                horizonBalance.Add((balance != null)?_mapper.Map<HorizonBalanceModel>(balance):null);

                foreach (var asset in _standardTokens) {
                    balance = accountResponse.Balances.FirstOrDefault(predicate: x => x.AssetCode == asset.AssetCode
                            && x.AssetIssuer == asset.AssetIssuerPublicKey);
                    horizonBalance.Add((balance != null)?_mapper.Map<HorizonBalanceModel>(balance):null);
                }
            }

            return horizonBalance;
        }

        public async Task<PaymentOperation> CreatePaymentOperationAsync(HorizonPaymentParameterModel model)
        {
            var sourceAccount = KeyPair.FromAccountId(model.SourceAccountPublicKey);
            var destinationAccount = KeyPair.FromAccountId(model.DestinationAccountPublicKey);

            Asset asset;

            if (model.AssetCode != null)
            {
                asset = new AssetTypeCreditAlphaNum4(model.AssetCode, model.AssetIssuerPublicKey);
            }
            else
            {
                asset = new AssetTypeNative();
            }

            return new PaymentOperation.Builder(destinationAccount, asset, model.Amount).SetSourceAccount(sourceAccount)
                .Build();
        }

        public Operation SetOptionsWeightOperation(string sourceAccountPublicKey, HorizonAccountWeightParameterModel weight)
        {
            var source = KeyPair.FromAccountId(sourceAccountPublicKey);
            var operation = new SetOptionsOperation.Builder();

            if (weight.MasterWeight >= 0)
            {
                operation.SetMasterKeyWeight(weight.MasterWeight);
            }

            if (weight.LowThreshold >= 0)
            {
                operation.SetLowThreshold(weight.LowThreshold);
            }

            if (weight.MediumThreshold >= 0)
            {
                operation.SetMediumThreshold(weight.MediumThreshold);
            }

            if (weight.HighThreshold >= 0)
            {
                operation.SetHighThreshold(weight.HighThreshold);
            }

            if (weight.PublicKeySigner != null)
            {
                operation.SetSigner(Signer.Ed25519PublicKey(KeyPair.FromAccountId(weight.PublicKeySigner.Signer)),
                    weight.PublicKeySigner.Weight);
            }

            if (weight.HashSigner != null)
            {
                var hash = Util.Hash(Encoding.UTF8.GetBytes(weight.HashSigner.Secret));
                operation.SetSigner(Signer.Sha256Hash(hash), weight.HashSigner.Weight);
            }

            operation.SetSourceAccount(source);

            return operation.Build();
        }

        public Operation SetOptionsSingleSignerOperation(string secondSignerAccountPublicKey)
        {
            var source = KeyPair.FromAccountId(secondSignerAccountPublicKey);
            var operation = new SetOptionsOperation.Builder();

            operation.SetSourceAccount(source);

            return operation.Build();
        }

        public Operation CreateAccountMergeOperation(string sourceAccountPublicKey, string destAccountPublicKey)
        {
            var source = KeyPair.FromAccountId(sourceAccountPublicKey);

            var operation = new AccountMergeOperation.Builder(KeyPair.FromAccountId(destAccountPublicKey)).SetSourceAccount(source)
                .Build();

            return operation;
        }

        public Operation ChangeTrustOperation(HorizonTrustParameterModel trustModel)
        {
            var source = KeyPair.FromAccountId(trustModel.SourceAccountPublicKey);
            Asset asset = new AssetTypeCreditAlphaNum4(trustModel.AssetCode, trustModel.AssetIssuerPublicKey);

            var operation = new ChangeTrustOperation.Builder(asset, trustModel.Limit).SetSourceAccount(source)
                .Build();

            return operation;
        }

        public Operation BumpSequenceOperation(string sourceAccountPublicKey, long nextSequence)
        {
            var source = KeyPair.FromAccountId(sourceAccountPublicKey);

            var operation = new BumpSequenceOperation.Builder(nextSequence).SetSourceAccount(source)
                .Build();

            return operation;
        }

        public Operation CreateAccountOperation(string sourceAccountPublicKey, string destAccountPublicey, string amount)
        {
            var source = KeyPair.FromAccountId(sourceAccountPublicKey);

            var dest = KeyPair.FromAccountId(destAccountPublicey);
            var operation = new CreateAccountOperation.Builder(dest, amount).SetSourceAccount(source)
                .Build();

            return operation;
        }

        public async Task<string> CreateTransaction(string sourceAccountPublicKey, List<Operation> operations, HorizonTimeBoundModel time, long sequence)
        {
            var accountResponse = await _horizonServerManager.GetAccountAsync(sourceAccountPublicKey);

            Transaction.Builder transactionBuilder;

            if (sequence == 0)
            {
                transactionBuilder = new Transaction.Builder(new Account(accountResponse.AccountId, accountResponse.SequenceNumber));
            }
            else
            {
                transactionBuilder = new Transaction.Builder(new Account(accountResponse.AccountId, sequence));
            }

            foreach (var operation in operations)
            {
                transactionBuilder.AddOperation(operation);
            }

            if (time != null)
            {
                transactionBuilder.AddTimeBounds(new TimeBounds(time.MinTime, time.MaxTime));
            }

            var transaction = transactionBuilder.Build();

            return transaction.ToUnsignedEnvelopeXdrBase64();
        }

        public string SignTransaction(string secretKey, string xdrTransaction)
        {
            var transaction = ConvertXdrToTransaction(xdrTransaction);
            var usableSecretSeed = KeyPair.FromSecretSeed(secretKey);

            transaction.Sign(usableSecretSeed);

            return transaction.ToEnvelopeXdrBase64();
        }

        public async Task<SubmitTransactionResponse> SubmitTransaction(string xdrTransaction)
        {
            var transaction = ConvertXdrToTransaction(xdrTransaction);

            return await _horizonServerManager.SubmitTransaction(transaction);
        }

        public string GetPublicKey(string secretKey)
        {
            var keyPair = KeyPair.FromSecretSeed(secretKey);

            return keyPair.AccountId;
        }

        public int GetSignatureCount(string xdrTransaction)
        {
            var transaction = ConvertXdrToTransaction(xdrTransaction);

            return transaction.Signatures.Count;
        }

        public string SignatureHash(string xdrTransaction, int index)
        {
            var transaction = ConvertXdrToTransaction(xdrTransaction);

            return Encoding.UTF8.GetString(transaction.Signatures[index]
                                               .Signature.InnerValue);
        }

        public async Task<bool> PaymentTransaction(HorizonPaymentParameterModel asset, string secretKey)
        {
            var operations = new List<Operation>();

            var paymentOperation = await CreatePaymentOperationAsync(asset);
            operations.Add(paymentOperation);

            var xdrTransaction = await CreateTransaction(asset.SourceAccountPublicKey, operations, time: null, sequence: 0);

            var signedTransaction = SignTransaction(secretKey, xdrTransaction);
            var response = await SubmitTransaction(signedTransaction);

            return response.IsSuccess();
        }

        private static Transaction ConvertXdrToTransaction(string transaction)
        {
            var bytes = Convert.FromBase64String(transaction);
            var transactionEnvelope = TransactionEnvelope.Decode(new XdrDataInputStream(bytes));

            return Transaction.FromEnvelopeXdr(transactionEnvelope);
        }
    }
}
