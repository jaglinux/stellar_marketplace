using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using stellar_dotnet_sdk;
using Stellmart.Api.Business.Managers.Interfaces;
using Stellmart.Api.Context.Entities;
using Stellmart.Api.Data.ContractPhase;
using Stellmart.Api.Data.Contracts;
using Stellmart.Api.Data.Enums;
using Stellmart.Api.Data.Horizon;
using Stellmart.Api.Data.Settings;
using Stellmart.Api.Services.ContractPhases;
using Stellmart.Api.Services.Interfaces;

namespace Stellmart.Api.Services
{
    public class ContractService : IContractService
    {

        //ToDo: add minimumFund in config
        private readonly IHorizonService _horizonService;

        private readonly string _minimumFund;
        private readonly IContractPhaseStrategy _contractPhaseStrategy;
        private readonly IContractDataManager _storeContratDataManager;
        private readonly ISignatureDataManager _signatureManager;
        private readonly IUserDataManager _userDataManager;
        private readonly HorizonKeyPairModel _worldSquareAccount;

        public ContractService(IHorizonService horizonService,
                               IOptions<ContractSettings> contractSettings,
                               IOptions<SignatureSettings> settings,
                               ISignatureDataManager signatureManager,
                               IContractPhaseStrategy contractPhaseStrategy,
                               IContractDataManager storeContratDataManager,
                               IUserDataManager userDataManager)
        {
            _userDataManager = userDataManager;
            _horizonService = horizonService;
            _minimumFund = contractSettings.Value.MinimumFund;
            _worldSquareAccount = new HorizonKeyPairModel { PublicKey = settings.Value.MasterPublicKey, SecretKey = settings.Value.MasterSecretKey };
            _signatureManager = signatureManager;
            _contractPhaseStrategy = contractPhaseStrategy;
            _storeContratDataManager = storeContratDataManager;
        }

        public async Task<Contract> SetupContractAsync(ContractParameterModel contractParameterModel)
        {
            var seller = await _userDataManager.GetByIdAsync(13);
            var escrow = _horizonService.CreateAccount();

            /* Create the escrow account with minimum fund, this is important
            * to register the account on network so that we obtain escrow sequence
            */
            var operations = new List<Operation>();
            //reduce funding amount
            //var createAccountOperation = _horizonService.CreateAccountOperation(_worldSquareAccount.PublicKey, escrow.PublicKey, "802");
            var createAccountOperation = _horizonService.CreateAccountOperation(_worldSquareAccount.PublicKey, escrow.PublicKey, "5");

            operations.Add(createAccountOperation);

            var xdrTransaction = await _horizonService.CreateTransaction(_worldSquareAccount.PublicKey, operations, time: null, sequence: 0);
            var response = await _horizonService.SubmitTransaction(_horizonService.SignTransaction(_worldSquareAccount.SecretKey, xdrTransaction));
            if (response.IsSuccess() == false)
            {
                Console.WriteLine("Failure: %s", response.ResultXdr);
                return null;
            }

            // Add WS as signer and assign escrow master weight to 0
            operations.Clear();
            var weightParam = new HorizonAccountWeightParameterModel
            {
                PublicKeySigner = new HorizonAccountSignerModel { Signer = _worldSquareAccount.PublicKey, Weight = 3 },
                MasterWeight = 0,
                LowThreshold = 6,
                MediumThreshold = 6,
                HighThreshold = 6
            };

            var setOptionsWeightOperation = _horizonService.SetOptionsWeightOperation(escrow.PublicKey, weightParam);
            operations.Add(setOptionsWeightOperation);

            // set buyer weight to 3

            weightParam = new HorizonAccountWeightParameterModel
            {
                PublicKeySigner = new HorizonAccountSignerModel { Signer = contractParameterModel.SourceAccountId, Weight = 3 }
            };

            setOptionsWeightOperation = _horizonService.SetOptionsWeightOperation(escrow.PublicKey, weightParam);
            operations.Add(setOptionsWeightOperation);

            // set seller weight to 1
            /*
            weightParam = new HorizonAccountWeightParameterModel
            {
                PublicKeySigner = new HorizonAccountSignerModel {Signer = seller.StellarPublicKey, Weight = 1}
            };

            setOptionsWeightOperation = _horizonService.SetOptionsWeightOperation(escrow.PublicKey, weightParam);
            operations.Add(setOptionsWeightOperation);*/

            xdrTransaction = await _horizonService.CreateTransaction(escrow.PublicKey, operations, time: null, sequence: 0);
            response = await _horizonService.SubmitTransaction(_horizonService.SignTransaction(escrow.SecretKey, xdrTransaction));
            if (response.IsSuccess() == false)
            {
                Console.WriteLine("Failure: %s", response.ResultXdr);
                return null;
            }
            //create contract
            var sequenceNumber = await _horizonService.GetSequenceNumberAsync(escrow.PublicKey);

            //de-couple phase and sequence numbering
            var sequenceAdd = 0;

            var contract = new Contract
            {
                EscrowAccountId = escrow.PublicKey,
                DestAccountId = contractParameterModel.DestinationAccountId,
                SourceAccountId = contractParameterModel.SourceAccountId,
                BaseSequenceNumber = sequenceNumber,
                CurrentSequenceNumber = sequenceNumber,
                CurrentPhaseNumber = 0,
                Phases = new List<ContractPhase>(),
                FundingAmount = contractParameterModel.FundingAmount,
                ContractStateId = (int)ContractStates.Initial,
                Id = 0,
                Obligation = contractParameterModel.Obligation
            };

            //create Phase ServiceInit regular and time over ride transactions and buyer signs it
            contract = await _contractPhaseStrategy.ConstructPhase(new ContractPhaseContext { Contract = contract, SequenceAdd = sequenceAdd, HorizonKeyPairModel = _worldSquareAccount },
                                                        PhaseTypes.SERVICE_INITIATION);
            //Funding is already completed; hence activate the contract
            contract.ContractStateId = (int)ContractStates.Activated;

            // sign first pre txn with ws
            var txn = contract.Phases.ToList().FirstOrDefault().Transactions.ToList().First();
            SignContract(new ContractSignatureModel()
            {
                Signature = new UserSignature()
                {
                    Transaction = txn,
                    PublicKey = _worldSquareAccount.PublicKey
                },
                Secret = _worldSquareAccount.SecretKey
            });
            //buyer sign first pre txn
            SignContract(new ContractSignatureModel()
            {
                Signature = new UserSignature()
                {
                    Transaction = txn,
                    PublicKey = contractParameterModel.SourceAccountId
                },
                Secret = contractParameterModel.SourceAccountSecret.Secret
            });

            //buyer signing not required, but we will create all pre txn here itself

            // sign each contract with ws signature
            contract = await _contractPhaseStrategy.ConstructPhase(new ContractPhaseContext { Contract = contract, SequenceAdd = ++sequenceAdd, HorizonKeyPairModel = _worldSquareAccount },
                                                        PhaseTypes.INTERMEDIARY);
            contract = await _contractPhaseStrategy.ConstructPhase(new ContractPhaseContext { Contract = contract, SequenceAdd = ++sequenceAdd, HorizonKeyPairModel = _worldSquareAccount },
                                                        PhaseTypes.RECEIPT);
            contract = await _contractPhaseStrategy.ConstructPhase(new ContractPhaseContext { Contract = contract, SequenceAdd = ++sequenceAdd, HorizonKeyPairModel = _worldSquareAccount },
                                                        PhaseTypes.DISPUTE);

            for (var i = 1; i < contract.Phases.Count; i++)
            {
                var phase = contract.Phases.ElementAt(i);
                foreach (var tx in phase.Transactions)
                {
                    SignContract(new ContractSignatureModel()
                    {
                        Signature = new UserSignature()
                        {
                            Transaction = tx,
                            PublicKey = _worldSquareAccount.PublicKey
                        },
                        Secret = _worldSquareAccount.SecretKey
                    });

                }
            }

            await _storeContratDataManager.SaveAsync(contract);

            return contract;
        }

        public string SignContract(ContractSignatureModel signatureModel)
        {
            // call horizon and if successful, then update signature and return true
            // otherwise return false
            var signature = signatureModel.Signature;
            var preTransaction = signature.Transaction;

            if (!Utils.VerifyTimeBound(preTransaction.MinimumTime, preTransaction.MaximumTime))
            {
                Console.WriteLine(value: "time bound delay verification failed");

                return "";
            }
            var secretKey = "";
            if (signature.PublicKey != null)
            {
                if (signatureModel.Secret == null)
                {
                    //system signature
                    secretKey = string.Copy(_worldSquareAccount.SecretKey);
                }
                else if (_horizonService.GetPublicKey(signatureModel.Secret) == signature.PublicKey)
                {
                    //user signature
                    secretKey = string.Copy(signatureModel.Secret);
                }
                else
                {
                    //secret key and public key did not match
                    Console.WriteLine(value: "secret key and public key did not match");

                }
            }
            var xdrString = _horizonService.SignTransaction(secretKey, preTransaction.XdrString);
            // Check if transaction is really signed
            preTransaction.XdrString = xdrString;
            preTransaction.Signatures.FirstOrDefault(predicate: x => x.PublicKey == signature.PublicKey).Signed = true;

            return xdrString;
        }

        public ContractPhase GetCurrentPhase(Contract contract)
        {
            return contract.Phases.ElementAt(contract.CurrentPhaseNumber - 1);
        }
        public async Task<bool> UpdateContractAsync(Contract contract, PhaseTypes phase)
        {
            var contractPhaseContext = new ContractPhaseContext { Contract = contract };
            return await _contractPhaseStrategy.UpdatePhase(contractPhaseContext, phase);
        }

    }
}
