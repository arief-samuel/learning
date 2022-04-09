using System.Threading.Tasks;
using AccountTransfer.Interfaces;
using Orleans;

namespace AccountTransfer.Grains
{
    public class AtmGrain : Grain, IAtmGrain
    {
        public Task Transfer(
            IAccountGrain fromAccount,
            IAccountGrain toAccount, 
            uint amountToTransfer) =>
            Task.WhenAll(
                fromAccount.Withdraw(amountToTransfer),
                toAccount.Deposit(amountToTransfer));
    }
}