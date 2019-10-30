using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Protocol;

namespace LightningHub.Controllers
{
    public abstract class LightningNodeApiController : Controller
    {
        [HttpPost("connect")]
        public async Task<IActionResult> ConnectToNode(ConnectToNodeRequest request)
        {
            NodeInfo nodeInfo;
            if (string.IsNullOrEmpty(request.NodeInfo))
            {
                if (!NodeInfo.TryParse(request.NodeInfo, out nodeInfo))
                {
                    ModelState.AddModelError(nameof(request.NodeId), "A valid node info was not provided to connect to");
                }
            }
            else
            {
                nodeInfo = new NodeInfo(new PubKey(request.NodeId), request.NodeHost, request.NodePort);
            }

            if (nodeInfo == null)
            {
                ModelState.AddModelError(nameof(request.NodeId), "A valid node info was not provided to connect to");
            }

            if (CheckValidation(out var errorActionResult))
            {
                return errorActionResult;
            }

            await GetLightningClient().ConnectTo(nodeInfo);
            return Ok();
        }

        [HttpGet("address")]
        public async Task<IActionResult> GetDepositAddress()
        {
            return Ok((await GetLightningClient().GetDepositAddress()).ToString());
        }

        [HttpPost("invoices/{invoice}/pay")]
        public async Task<IActionResult> PayInvoice(string invoice)
        {
            try
            {
                BOLT11PaymentRequest.TryParse(invoice, out var bolt11PaymentRequest, GetNetwork());
            }
            catch (Exception e)
            {
                ModelState.AddModelError(nameof(invoice), "The BOLT11 invoice was invalid.");
            }
            if (CheckValidation(out var errorActionResult))
            {
                return errorActionResult;
            }
            return Json(await GetLightningClient().Pay(invoice));
        }

        [HttpPost("invoices")]
        public async Task<IActionResult> CreateInvoice(CreateInvoiceRequest request)
        {
            if (CheckValidation(out var errorActionResult))
            {
                return errorActionResult;
            }
            return Ok(await GetLightningClient()
                .CreateInvoice(request.Amount, request.Description, request.Expiry, CancellationToken.None));
        }

        private bool CheckValidation(out IActionResult result)
        {
            if (!ModelState.IsValid)
            {
                result =  BadRequest(ModelState);
                return true;
            }

            result = null;
            return false;
        }

        protected abstract ILightningClient GetLightningClient();
        protected abstract Network GetNetwork();
    }

    public class ConnectToNodeRequest
    {
        public string NodeInfo { get; set; }
        public string NodeId { get; set; }
        public string NodeHost { get; set; }
        public int NodePort { get; set; }
    }

    public class CreateInvoiceRequest
    {
        public LightMoney Amount { get; set; }
        public string Description { get; set; }
        public TimeSpan Expiry { get; set; }
    }
}