using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LightningHub.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace LightningHub.Controllers
{
    public class TokenManager
    {
        private readonly IMemoryCache _memoryCache;
        private readonly UserManager<ApplicationUser> _userManager;

        public TokenManager(IMemoryCache memoryCache, UserManager<ApplicationUser> _userManager)
        {
            _memoryCache = memoryCache;
            _userManager = _userManager;
        }

        public AuthorizeResponseAuth IssueAccessToken(ApplicationUser user)
        {
            var newToken = new Token()
            {
                Expiry = DateTime.Now.AddHours(24),
                AccessToken = Guid.NewGuid().ToString(),
                RefreshToken = Guid.NewGuid().ToString(),
                UserId = user.Id
            };

            _memoryCache.CreateEntry(newToken.AccessToken).Value = newToken;
            _memoryCache.CreateEntry(newToken.RefreshToken).Value = newToken;
            return new AuthorizeResponseAuth()
            {
                AccessToken = newToken.AccessToken,
                RefreshToken = newToken.RefreshToken,
                TokenType = "magic",
                Expiry = newToken.Expiry.ToString("O")
            };
        }

        public bool IsValid(string accessToken, out string userId)
        {
            if (_memoryCache.TryGetValue<Token>(accessToken, out var auth))
            {
                userId = auth.UserId;
                return true;
            }

            userId = null;
            return false;
        }

        public async Task<AuthorizeResponseAuth> Authorize(AuthorizeRequest request)
        {
            if (!string.IsNullOrEmpty(request.RefreshToken))
            {
                if (_memoryCache.TryGetValue<Token>(request.RefreshToken, out var auth))
                {
                    _memoryCache.Remove(request.RefreshToken);
                    if (_memoryCache.TryGetValue(auth.AccessToken, out var auth2))
                    {
                        _memoryCache.Remove(auth2);
                    }

                    var user = await _userManager.FindByIdAsync(auth.UserId);
                    if (user == null)
                    {
                        return null;
                    }

                    return IssueAccessToken(user);
                }

                return null;
            }
            else
            {
                var user = await _userManager.FindByNameAsync(request.Login);
                if (user == null)
                {
                    return null;
                }

                var passCheck = await _userManager.CheckPasswordAsync(user, request.Password);
                if (passCheck)
                {
                    return IssueAccessToken(user);
                }
            }

            return null;
        }

        public class Token
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public string UserId { get; set; }
            public DateTime Expiry { get; set; }
        }
    }

    public class LightningHubRepository
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public LightningHubRepository(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<IEnumerable<Transaction>> GetTransactions(TransactionQuery query)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();
            return await context.Transactions
                .Where(transaction =>
                    !query.PaymentTypes.Any() || query.PaymentTypes.Contains(transaction.PaymentType))
                .Where(transaction =>
                    !query.TransferTypes.Any() || query.TransferTypes.Contains(transaction.TransferType))
                .Where(transaction =>
                    !query.TransactionStatuses.Any() || query.TransactionStatuses.Contains(transaction.Status))
                .Where(transaction => !query.UserIds.Any() || query.UserIds.Contains(transaction.UserId))
                .ToListAsync();
        }

        public class TransactionQuery
        {
            public HashSet<string> UserIds { get; set; } = new HashSet<string>();
            public HashSet<PaymentType> PaymentTypes { get; set; } = new HashSet<PaymentType>();
            public HashSet<TransferType> TransferTypes { get; set; } = new HashSet<TransferType>();
            public HashSet<TransactionStatus> TransactionStatuses { get; set; } = new HashSet<TransactionStatus>();
        }
    }

    /// LNDHub Compatible api 
    /// </summary>
    [Route("lndhub")]
    public class LndHubController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly TokenManager _tokenManager;
        private readonly LightningClientProvider _lightningClientProvider;
        private readonly LightningHubRepository _lightningHubRepository;

        public LndHubController(UserManager<ApplicationUser> userManager,
            TokenManager tokenManager, LightningClientProvider lightningClientProvider, LightningHubRepository lightningHubRepository)
        {
            _userManager = userManager;
            _tokenManager = tokenManager;
            _lightningClientProvider = lightningClientProvider;
            _lightningHubRepository = lightningHubRepository;
        }

        /// <summary>
        /// Create new user account and get credentials
        /// </summary>
        /// <returns></returns>
        [HttpPost("create")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateAccount(CreateNewAccountRequest request)
        {
            var password = Guid.NewGuid().ToString();
            var user = new ApplicationUser()
            {
                UserName = Guid.NewGuid().ToString(),
                AccountType = request.AccountType,
                PartnerId = request.PartnerId
            };
            var userResult = await _userManager.CreateAsync(user);
            if (!userResult.Succeeded) return IdentityResultErrorToActionResult(userResult);
            userResult = await _userManager.AddPasswordAsync(user, password);
            if (!userResult.Succeeded)
                return IdentityResultErrorToActionResult(userResult);

            return Json(new CreateNewAccountResponse()
            {
                Login = user.UserName,
                Password = password
            });
        }

        /// <summary>
        /// Authorize user with Oauth. When user use refresh_token to auth, then this refresh_token not available for access once again. Use new refresh_token
        /// </summary>
        /// <returns></returns>
        [HttpPost("auth")]
        public async Task<IActionResult> Authorize(string type, AuthorizeRequest request)
        {
            var result = await _tokenManager.Authorize(request);
            if (result == null)
            {
                return GetErrorResponse(ErrorType.BadAuth, "");
            }

            return Json(result);
        }

        [HttpGet("getbtc")]
        public async Task<IActionResult> GetOnchainAddress()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user.Addresses.Any())
            {
                return Json(new GetAddressResponse
                {
                    Address = user.Addresses.Last()
                });
            }

            return await GetNewOnchainAddress();
        }

        [HttpGet("newbtc")]
        public async Task<IActionResult> GetNewOnchainAddress()
        {
            var user = await _userManager.GetUserAsync(User);
            var address = await _lightningClientProvider.GetLightningClient().GetDepositAddress();

            user.Addresses.Add(address.ToString());
            await _userManager.UpdateAsync(user);

            return Json(new GetAddressResponse
            {
                Address = address.ToString()
            });
        }

        [HttpGet("getpending")]
        public async Task<IActionResult> GetPendingBalance()
        {
            var user = await _userManager.GetUserAsync(User);
            var txs = await _lightningHubRepository.GetTransactions(new LightningHubRepository.TransactionQuery()
            {
                UserIds = new HashSet<string>()
                {
                    user.Id
                },
                PaymentTypes = new HashSet<PaymentType>()
                {
                    PaymentType.Onchain
                },
                TransferTypes = new HashSet<TransferType>()
                {
                    TransferType.Receive
                },
                TransactionStatuses = new HashSet<TransactionStatus>()
                {
                    TransactionStatus.Pending
                }
            });
            
            return Json(new BalanceResponseItem()
            {
                
            })
        }

        [HttpGet("decodeinvoice")]
        public async Task<IActionResult> DecodeInvoice()
        {
        }

        [HttpGet("checkroute")]
        public async Task<IActionResult> CheckRoute()
        {
        }

        [HttpPost("payinvoice")]
        public async Task<IActionResult> PayInvoice()
        {
        }

        [HttpPost("sendcoins")]
        public async Task<IActionResult> SendCoins()
        {
        }

        [HttpPost("payinvoice")]
        public async Task<IActionResult> PayInvoice()
        {
        }

        [HttpGet("gettxs")]
        public async Task<IActionResult> GetTransactions(int limit = 10, int offset = 0)
        {
        }

        [HttpGet("gettx")]
        public async Task<IActionResult> GetTransaction(int txid)
        {
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            var user = await _userManager.GetUserAsync(User);
            return Ok(user.Balance);
        }

        [HttpPost("getinfo")]
        public async Task<IActionResult> GetInfo()
        {
        }

        [HttpPost("addinvoice")]
        public async Task<IActionResult> CreateInvoice()
        {
        }

        [HttpGet("getuserinvoices")]
        public async Task<IActionResult> GetUserInvoices()
        {
        }


        private IActionResult IdentityResultErrorToActionResult(IdentityResult identityResult)
        {
            return GetErrorResponse(ErrorType.ServerError,
                identityResult.Errors.Select(error => $"{error.Code}:{error.Description}").ToArray().Join());
        }

        private IActionResult GetSuccessResponse()
        {
            return Json(new
            {
                ok = true
            });
        }

        private IActionResult GetErrorResponse(ErrorType type, string message)
        {
            return Json(new
            {
                error = true,
                code = (int) type,
                message
            });
        }

        enum ErrorType
        {
            BadAuth = 1,
            NotEnoughBalance = 2,
            BadPartner = 3,
            InvalidInvoice = 4,
            RouteNotFound = 5,
            ServerError = 6,
            LNFailure = 7
        }
    }

    public class CreateNewAccountRequest
    {
        public string PartnerId { get; set; }
        public string AccountType { get; set; } = "common";
    }

    public class CreateNewAccountResponse
    {
        public string Login { get; set; }
        public string Password { get; set; }
    }

    public class AuthorizeRequest
    {
        public string Login { get; set; }
        public string Password { get; set; }
        public string RefreshToken { get; set; }
    }

    public class OAuth2TokenAuthorizeRequest
    {
        public string GrantType { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
    }

    public class AuthorizeResponseAuth
    {
        [JsonProperty("access_token")] public string AccessToken { get; set; }
        [JsonProperty("token_type")] public string TokenType { get; set; }
        [JsonProperty("refresh_token")] public string RefreshToken { get; set; }
        [JsonProperty("expiry")] public string Expiry { get; set; }
    }

    public class GetAddressResponse
    {
        public string Address { get; set; }
    }

    public class InvoiceRequest
    {
        public string Invoice { get; set; }
    }

    public class InvoiceResponse
    {
        public string Type { get; set; }
        [JsonProperty("txid")] public string TransactionId { get; set; }
        [JsonProperty("amt")] public long Amount { get; set; }
        [JsonProperty("fee")] public long Fee { get; set; }
        [JsonProperty("timestamp")] public long Timestamp { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Description { get; set; }
        public string Invoice { get; set; }
    }

    public class PayInvoiceResponse
    {
        [JsonProperty("payment_error")] public string PaymentError { get; set; }

        [JsonProperty("payment_preimage")] public string PaymentPreimage { get; set; }

        [JsonProperty("payment_route")] public PaymentRoute PaymentRoute { get; set; }
    }

    public partial class PaymentRoute
    {
        [JsonProperty("total_time_lock")] public long TotalTimeLock { get; set; }

        [JsonProperty("total_fees")] public long TotalFees { get; set; }

        [JsonProperty("total_amt")] public long TotalAmt { get; set; }

        [JsonProperty("total_fees_msat")] public long TotalFeesMsat { get; set; }

        [JsonProperty("total_amt_msat")] public long TotalAmtMsat { get; set; }

        [JsonProperty("hops")] public List<Hop> Hops { get; set; }
    }

    public partial class Hop
    {
        [JsonProperty("chan_id")] public long ChanId { get; set; }

        [JsonProperty("chan_capacity")] public long ChanCapacity { get; set; }

        [JsonProperty("amt_to_forward")] public long AmtToForward { get; set; }

        [JsonProperty("fee")] public long Fee { get; set; }

        [JsonProperty("expiry")] public long Expiry { get; set; }

        [JsonProperty("amt_to_forward_msat")] public long AmtToForwardMsat { get; set; }

        [JsonProperty("fee_msat")] public long FeeMsat { get; set; }
    }

    public class CheckRouteRequest
    {
        public string Destination { get; set; }
        public string Amount { get; set; }
    }


    public class BalanceResponseItem
    {
        public long TotalBalance { get; set; }
        public long AvailableBalance { get; set; }
        public long UnconfirmedBalance { get; set; }
    }

    public class GetInfoResponse
    {
        [JsonProperty("fee")] public long Fee { get; set; }

        [JsonProperty("identity_pubkey")] public string IdentityPubkey { get; set; }

        [JsonProperty("alias")] public string Alias { get; set; }

        [JsonProperty("num_pending_channels")] public long NumPendingChannels { get; set; }

        [JsonProperty("num_active_channels")] public long NumActiveChannels { get; set; }

        [JsonProperty("num_peers")] public long NumPeers { get; set; }

        [JsonProperty("block_height")] public long BlockHeight { get; set; }

        [JsonProperty("block_hash")] public string BlockHash { get; set; }

        [JsonProperty("synced_to_chain")] public bool SyncedToChain { get; set; }

        [JsonProperty("testnet")] public bool Testnet { get; set; }

        [JsonProperty("chains")] public List<string> Chains { get; set; }

        [JsonProperty("uris")] public List<string> Uris { get; set; }

        [JsonProperty("best_header_timestamp")]
        public string BestHeaderTimestamp { get; set; }

        [JsonProperty("version")] public string Version { get; set; }
    }
}