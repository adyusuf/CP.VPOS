﻿using CP.VPOS.Enums;
using CP.VPOS.Helpers;
using CP.VPOS.Interfaces;
using CP.VPOS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace CP.VPOS.Banks.Sipay
{
    internal class SipayVirtualPOSService : IVirtualPOSService
    {
        private readonly string _urlAPITest = "https://provisioning.sipay.com.tr/ccpayment";
        private readonly string _urlAPILive = "https://app.sipay.com.tr/ccpayment";

        public SaleResponse Sale(SaleRequest request, VirtualPOSAuth auth)
        {
            SaleResponse response = new SaleResponse();

            request.saleInfo.currency = request.saleInfo.currency ?? Currency.TRY;
            request.saleInfo.installment = request.saleInfo.installment > 1 ? request.saleInfo.installment : (sbyte)1;

            if (request?.payment3D?.confirm == true)
                return Sale3D(request, auth);

            response.orderNumber = request.orderNumber;

            SipayTokenModel _token = null;

            try
            {
                _token = GetTokenModel(auth);
            }
            catch (Exception ex)
            {
                response.statu = SaleResponseStatu.Error;
                response.message = ex.Message;

                return response;
            }


            string totalStr = request.saleInfo.amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")).Replace(".", "").Replace(",", ".");
            string installmentStr = request.saleInfo.installment.ToString();

            string name = request.invoiceInfo?.name;
            string surname = request.invoiceInfo?.surname;

            if (string.IsNullOrWhiteSpace(name))
                name = "[boş]";

            if (string.IsNullOrWhiteSpace(surname))
                surname = "[boş]";

            Dictionary<string, object> req = new Dictionary<string, object> {
                {"cc_holder_name", request.saleInfo.cardNameSurname },
                {"cc_no", request.saleInfo.cardNumber},
                {"expiry_month", request.saleInfo.cardExpiryDateMonth.cpToString() },
                {"expiry_year", request.saleInfo.cardExpiryDateYear.cpToString() },
                {"cvv",request.saleInfo.cardCVV},
                {"currency_code", request.saleInfo.currency.ToString() },
                {"installments_number", installmentStr },
                {"invoice_id", request.orderNumber },
                {"invoice_description", $"{request.orderNumber} nolu sipariş ödemesi" },
                {"name", name },
                {"surname", surname },
                {"total", totalStr },
                {"merchant_key", auth.merchantStorekey },
                {"transaction_type", "Auth" },
                {"items", new List<object>()
                {
                    new
                    {
                        name = "Tahsilat",
                        price = totalStr,
                        quantity = 1,
                        description = "Tahsilat"
                    }
                }},
                {"hash_key", "" }
            };

            //string hash_key = GenerateHashKey(totalStr, installmentStr, request.saleInfo.currency.ToString(), auth.merchantStorekey, request.orderNumber, auth.merchantPassword);

            var data = totalStr + "|" + installmentStr + "|" + request.saleInfo.currency.ToString() + "|" + auth.merchantStorekey + "|" + request.orderNumber;
            string hash_key = GenerateHashKey(data, auth.merchantPassword);

            req["hash_key"] = hash_key;



            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/api/paySmart2D";

            string responseStr = Request(req, link, _token);

            Dictionary<string, object> responseDic = JsonConvertHelper.Convert<Dictionary<string, object>>(responseStr);

            response.privateResponse = responseDic;

            if (responseDic?.ContainsKey("status_code") == true && responseDic["status_code"].cpToString() == "100")
            {
                string transactionId = "";

                try
                {
                    if (responseDic.ContainsKey("data"))
                    {
                        var dataObj = JsonConvertHelper.Convert<Dictionary<string, object>>(JsonConvertHelper.Json(responseDic["data"]));

                        if (dataObj?.ContainsKey("auth_code") == true)
                            transactionId = dataObj["auth_code"].cpToString();
                    }
                }
                catch { }


                response.statu = SaleResponseStatu.Success;
                response.message = "İşlem başarılı";
                response.transactionId = transactionId;

                return response;
            }

            string errorMsg = "İşlem sırasında bir hata oluştu";

            if (responseDic?.ContainsKey("status_description") == true && responseDic["status_description"].cpToString() != "")
                errorMsg = responseDic["status_description"].cpToString();

            response.statu = SaleResponseStatu.Error;
            response.message = errorMsg;

            return response;
        }

        private SaleResponse Sale3D(SaleRequest request, VirtualPOSAuth auth)
        {
            SaleResponse response = new SaleResponse();
            response.orderNumber = request.orderNumber;


            SipayTokenModel _token = null;

            try
            {
                _token = GetTokenModel(auth);
            }
            catch (Exception ex)
            {
                response.statu = SaleResponseStatu.Error;
                response.message = ex.Message;

                return response;
            }

            string totalStr = request.saleInfo.amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")).Replace(".", "").Replace(",", ".");
            string installmentStr = request.saleInfo.installment.ToString();


            string name = request.invoiceInfo?.name;
            string surname = request.invoiceInfo?.surname;

            if (string.IsNullOrWhiteSpace(name))
                name = "[boş]";

            if (string.IsNullOrWhiteSpace(surname))
                surname = "[boş]";

            var threedmode = request.payment3D.threeDMode == ThreeDMode.ThreeDPay ? "app" : "merchant";

            Dictionary<string, object> req = new Dictionary<string, object> {
                {"cc_holder_name", request.saleInfo.cardNameSurname },
                {"cc_no", request.saleInfo.cardNumber},
                {"expiry_month", request.saleInfo.cardExpiryDateMonth.cpToString() },
                {"expiry_year", request.saleInfo.cardExpiryDateYear.cpToString() },
                {"cvv",request.saleInfo.cardCVV},
                {"currency_code", request.saleInfo.currency.ToString() },
                {"installments_number", installmentStr },
                {"invoice_id", request.orderNumber },
                {"invoice_description", $"{request.orderNumber} nolu sipariş ödemesi" },
                {"name", name },
                {"surname", surname },
                {"total", totalStr },
                {"merchant_key", auth.merchantStorekey },
                {"transaction_type", "Auth" },
                {"items", new List<object>()
                {
                    new
                    {
                        name = "Tahsilat",
                        price = totalStr,
                        quantity = 1,
                        description = "Tahsilat"
                    }
                }},
                {"hash_key", "" },
                {"response_method", "POST" },
                {"payment_completed_by", threedmode },
                {"ip", request.customerIPAddress },
                {"cancel_url", request.payment3D.returnURL },
                {"return_url", request.payment3D.returnURL },
            };

            //string hash_key = GenerateHashKey(totalStr, installmentStr, request.saleInfo.currency.ToString(), auth.merchantStorekey, request.orderNumber, auth.merchantPassword);

            var data = totalStr + "|" + installmentStr + "|" + request.saleInfo.currency.ToString() + "|" + auth.merchantStorekey + "|" + request.orderNumber;
            string hash_key = GenerateHashKey(data, auth.merchantPassword);
            req["hash_key"] = hash_key;


            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/api/paySmart3D";

            string responseStr = Request(req, link, _token);


            response.statu = SaleResponseStatu.RedirectHTML;
            response.message = responseStr;

            return response;
        }

        public SaleResponse Sale3DResponse(Sale3DResponseRequest request, VirtualPOSAuth auth)
        {
            SaleResponse response = new SaleResponse();

            response.privateResponse = request?.responseArray;

            bool hashValid = false;

            if (request?.responseArray?.ContainsKey("md_status") == true)
                response.mdStatus = request.responseArray["md_status"].ToString();

            if (request?.responseArray?.ContainsKey("auth_code") == true)
                response.transactionId = request.responseArray["auth_code"].ToString();

            if (request?.responseArray?.ContainsKey("invoice_id") == true)
                response.orderNumber = request.responseArray["invoice_id"].ToString();

            if (request?.responseArray?.ContainsKey("order_id") == true)
                response.orderId = request.responseArray["order_id"].ToString();

            try
            {
                if (request?.responseArray?.ContainsKey("hash_key") == true)
                {
                    var validateHashKey = ValidateHashKey(request.responseArray["hash_key"].ToString(), auth.merchantPassword);

                    if (validateHashKey?.Any(s => s == response.orderNumber) == true)
                        hashValid = true;
                }
            }
            catch { }

            if (hashValid == false)
            {
                response.statu = SaleResponseStatu.Error;
                response.message = "Hash doğrulanamadı, ödeme onaylanmadı.";
            }
            else if (request?.responseArray?.ContainsKey("status_code") == true && request.responseArray["status_code"].ToString() == "100")
            {
                response.statu = SaleResponseStatu.Success;
                response.message = "İşlem başarılı";
            }
            //3d Pending
            else if (request?.responseArray?.ContainsKey("status_code") == true && request.responseArray["status_code"].ToString() == "69")
            {
                response.statu = SaleResponseStatu.Pending;
                response.message = "İşlem henüz tamamlanmadı";
            }
            //3d cancel
            else if (request?.responseArray?.ContainsKey("status_code") == true && request.responseArray["status_code"].ToString() == "41")
            {
                response.statu = SaleResponseStatu.Error;
                response.message = "3D doğrulama iptal edildi";
            }
            else if (request?.responseArray?.ContainsKey("error") == true && request.responseArray["error"].ToString() != "")
            {
                response.statu = SaleResponseStatu.Error;
                response.message = request.responseArray["error"].ToString();
            }
            else
            {
                response.statu = SaleResponseStatu.Error;
                response.message = "İşlem sırasında bir hata oluştu";
            }

            return response;
        }



        public SaleResponse Commit(CommitRequest request, VirtualPOSAuth auth)
        {
            SaleResponse response = new SaleResponse { statu = SaleResponseStatu.Error };

            SipayTokenModel _token = null;

            try
            {
                _token = GetTokenModel(auth);
            }
            catch (Exception ex)
            {
                response.statu = SaleResponseStatu.Error;
                response.message = ex.Message;

                return response;
            }

            Dictionary<string, object> req = new Dictionary<string, object> {
                {"merchant_key", auth.merchantStorekey },
                {"invoice_id", request.orderNumber },
                {"order_id", request.orderId },
                {"status", "complete" },
                {"hash_key", "" }
            };

            var s = auth.merchantStorekey + "|" + request.orderNumber + "|" + request.orderId + "|" + "complete";

            string hash_key = GenerateHashKey(s, auth.merchantPassword);

            req["hash_key"] = hash_key;

            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/payment/complete";

            string responseStr = Request(req, link, _token);

            Sipay3DCompleteModel completeResponse = JsonConvertHelper.Convert<Sipay3DCompleteModel>(responseStr);
            Dictionary<string, object> responseDic = JsonConvertHelper.Convert<Dictionary<string, object>>(responseStr);

            //completeResponse.data.Add("status_code", completeResponse.status_code);
            //completeResponse.data.Add("status_description", completeResponse.status_description);

            response.privateResponse = completeResponse.data;

            if (completeResponse.status_code == 100)
            {
                response.statu = SaleResponseStatu.Success;
                response.message = "İşlem başarılı";
            }
            else if (completeResponse.status_description != "")
            {
                response.statu = SaleResponseStatu.Error;
                response.message = completeResponse.status_description;
            }
            else
            {
                response.statu = SaleResponseStatu.Error;
                response.message = "İşlem tamamlanamadı";
            }

            return response;
        }

        public CancelResponse Cancel(CancelRequest request, VirtualPOSAuth auth)
        {
            CancelResponse response = new CancelResponse { statu = ResponseStatu.Error };

            SipayTokenModel _token = null;

            try
            {
                _token = GetTokenModel(auth);
            }
            catch (Exception ex)
            {
                response.statu = ResponseStatu.Error;
                response.message = ex.Message;

                return response;
            }


            Dictionary<string, object> req = new Dictionary<string, object> {
                {"invoice_id", request.orderNumber },
                {"amount", 0 },
                {"app_id", auth.merchantUser },
                {"app_secret", auth.merchantPassword },
                {"merchant_key", auth.merchantStorekey },
                {"hash_key", "" }
            };


            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/api/refund";

            string responseStr = Request(req, link, _token);

            Dictionary<string, object> responseDic = JsonConvertHelper.Convert<Dictionary<string, object>>(responseStr);

            response.privateResponse = responseDic;


            if (responseDic?.ContainsKey("status_code") == true && responseDic["status_code"].cpToString() == "100")
            {
                response.statu = ResponseStatu.Success;
                response.message = "İşlem başarılı";
            }
            else if (responseDic?.ContainsKey("status_description") == true && responseDic["status_description"].cpToString() != "")
            {
                response.statu = ResponseStatu.Error;
                response.message = responseDic["status_description"].cpToString();
            }
            else
            {
                response.statu = ResponseStatu.Error;
                response.message = "İşlem iade edilemedi";
            }

            return response;
        }

        public RefundResponse Refund(RefundRequest request, VirtualPOSAuth auth)
        {
            RefundResponse response = new RefundResponse { statu = ResponseStatu.Error };

            SipayTokenModel _token = null;

            try
            {
                _token = GetTokenModel(auth);
            }
            catch (Exception ex)
            {
                response.statu = ResponseStatu.Error;
                response.message = ex.Message;

                return response;
            }


            Dictionary<string, object> req = new Dictionary<string, object> {
                {"invoice_id", request.orderNumber },
                {"amount", request.refundAmount },
                {"app_id", auth.merchantUser },
                {"app_secret", auth.merchantPassword },
                {"merchant_key", auth.merchantStorekey },
                {"hash_key", "" }
            };


            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/api/refund";

            string responseStr = Request(req, link, _token);

            Dictionary<string, object> responseDic = JsonConvertHelper.Convert<Dictionary<string, object>>(responseStr);

            response.privateResponse = responseDic;


            if (responseDic?.ContainsKey("status_code") == true && responseDic["status_code"].cpToString() == "100")
            {
                response.statu = ResponseStatu.Success;
                response.message = "İşlem başarılı";
                response.refundAmount = request.refundAmount;
            }
            else if (responseDic?.ContainsKey("status_description") == true && responseDic["status_description"].cpToString() != "")
            {
                response.statu = ResponseStatu.Error;
                response.message = responseDic["status_description"].cpToString();
            }
            else
            {
                response.statu = ResponseStatu.Error;
                response.message = "İşlem iade edilemedi";
            }

            return response;
        }

        public AdditionalInstallmentQueryResponse AdditionalInstallmentQuery(AdditionalInstallmentQueryRequest request, VirtualPOSAuth auth)
        {
            return null;
        }

        public AllInstallmentQueryResponse AllInstallmentQuery(AllInstallmentQueryRequest request, VirtualPOSAuth auth)
        {
            AllInstallmentQueryResponse response = new AllInstallmentQueryResponse { confirm = false, installmentList = new List<AllInstallment>() };

            SipayTokenModel _token = null;

            try
            {
                _token = GetTokenModel(auth);
            }
            catch
            {
                return response;
            }

            Dictionary<string, object> req = new Dictionary<string, object> {
                {"currency_code", (request.currency ?? Currency.TRY).ToString() }
            };


            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/api/commissions";

            string responseStr = Request(req, link, _token);

            try
            {
                Dictionary<string, object> responseDic = JsonConvertHelper.Convert<Dictionary<string, object>>(responseStr);

                if (responseDic?.ContainsKey("status_code") == true && responseDic["status_code"].cpToString() == "100")
                {
                    response.confirm = true;

                    if (responseDic?.ContainsKey("data") == true)
                    {
                        Dictionary<string, object> keyValuePairs = JsonConvertHelper.Convert<Dictionary<string, object>>(JsonConvertHelper.Json<object>(responseDic["data"]));

                        if (keyValuePairs?.Any() == true)
                        {
                            foreach (var item in keyValuePairs)
                            {
                                int installment_number = item.Key.cpToInt();

                                List<Dictionary<string, object>> installmentList = JsonConvertHelper.Convert<List<Dictionary<string, object>>>(JsonConvertHelper.Json<object>(item.Value));

                                foreach (Dictionary<string, object> installmentModel in installmentList)
                                {

                                    if (installmentModel?.ContainsKey("user_commission_percentage") == true && installmentModel["user_commission_percentage"].cpToString() != "x" && installmentModel.ContainsKey("getpos_card_program") == true && installmentModel["getpos_card_program"].cpToString() != "")
                                    {
                                        CreditCardProgram creditCardProgram = CreditCardProgram.Unknown;
                                        string getpos_card_program = installmentModel["getpos_card_program"].cpToString();
                                        float user_commission_percentage = installmentModel["user_commission_percentage"].cpToSingle();

                                        switch (getpos_card_program)
                                        {
                                            case "MAXIMUM": creditCardProgram = CreditCardProgram.Maximum; break;
                                            case "BANKKART_COMBO": creditCardProgram = CreditCardProgram.Bankkart; break;
                                            case "WORLD": creditCardProgram = CreditCardProgram.World; break;
                                            case "PARAF": creditCardProgram = CreditCardProgram.Paraf; break;
                                            case "BONUS": creditCardProgram = CreditCardProgram.Bonus; break;
                                            case "AXESS": creditCardProgram = CreditCardProgram.Axess; break;
                                            case "WINGS": creditCardProgram = CreditCardProgram.Wings; break;
                                            case "CARD_FNS": creditCardProgram = CreditCardProgram.CardFinans; break;
                                            case "ADVANT": creditCardProgram = CreditCardProgram.Advantage; break;
                                            case "MILES&SMILES": creditCardProgram = CreditCardProgram.MilesAndSmiles; break;

                                            default:
                                                creditCardProgram = CreditCardProgram.Unknown;
                                                break;
                                        }

                                        if (creditCardProgram == CreditCardProgram.Unknown)
                                            continue;

                                        AllInstallment model = new AllInstallment
                                        {
                                            bankCode = "9991",
                                            cardProgram = creditCardProgram,
                                            count = installment_number,
                                            customerCostCommissionRate = user_commission_percentage
                                        };

                                        response.installmentList.Add(model);
                                    }
                                    else
                                        continue;

                                }
                            }

                        }
                    }
                }
            }
            catch { }

            return response;
        }

        public BINInstallmentQueryResponse BINInstallmentQuery(BINInstallmentQueryRequest request, VirtualPOSAuth auth)
        {
            BINInstallmentQueryResponse response = new BINInstallmentQueryResponse();

            SipayTokenModel _token = null;

            try
            {
                _token = GetTokenModel(auth);
            }
            catch
            {
                return response;
            }


            Dictionary<string, object> req = new Dictionary<string, object> {
                {"credit_card", request.BIN },
                {"amount", request.amount },
                {"currency_code", (request.currency ?? Currency.TRY).ToString() },
                {"merchant_key", auth.merchantStorekey },
            };


            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/api/getpos";

            string responseStr = Request(req, link, _token);

            try
            {
                Dictionary<string, object> responseDic = JsonConvertHelper.Convert<Dictionary<string, object>>(responseStr);

                if (responseDic?.ContainsKey("status_code") == true && responseDic["status_code"].cpToString() == "100")
                {
                    response.confirm = true;

                    if (responseDic?.ContainsKey("data") == true)
                    {
                        List<Dictionary<string, object>> keyValuePairs = JsonConvertHelper.Convert<List<Dictionary<string, object>>>(JsonConvertHelper.Json<object>(responseDic["data"]));

                        if (keyValuePairs?.Any() == true)
                        {
                            response.installmentList = new List<installment>();

                            foreach (var item in keyValuePairs)
                            {
                                int installments_number = item["installments_number"].cpToInt();
                                decimal payable_amount = item["payable_amount"].cpToDecimal();
                                float commissionRate = 0;

                                if (installments_number > 1)
                                {
                                    if (payable_amount > request.amount)
                                        commissionRate = ((((decimal)100 * payable_amount) / request.amount) - (decimal)100).cpToSingle();

                                    response.installmentList.Add(new
                                    installment
                                    {
                                        count = installments_number,
                                        customerCostCommissionRate = commissionRate
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }


            return response;
        }



        private SipayTokenModel GetTokenModel(VirtualPOSAuth auth)
        {
            SipayTokenModel token = null;

            Dictionary<string, object> postData = new Dictionary<string, object>
            {
                { "app_id", auth.merchantUser },
                { "app_secret", auth.merchantPassword }
            };

            string link = $"{(auth.testPlatform ? _urlAPITest : _urlAPILive)}/api/token";

            string loginResponse = Request(postData, link);

            Dictionary<string, object> loginDic = JsonConvertHelper.Convert<Dictionary<string, object>>(loginResponse);

            if (loginDic?.ContainsKey("status_code") == true && loginDic["status_code"].cpToString() == "100" && loginDic?.ContainsKey("data") == true)
            {
                token = JsonConvertHelper.Convert<SipayTokenModel>(JsonConvertHelper.Json(loginDic["data"]));

                if (!string.IsNullOrWhiteSpace(token?.token))
                    return token;
            }

            string errorMsg = "Sipay token error";

            if (loginDic?.ContainsKey("status_description") == true && loginDic["status_description"].cpToString() != "")
                errorMsg = errorMsg + " - " + loginDic["status_description"].cpToString();

            throw new Exception(errorMsg);
        }

        private string Request(Dictionary<string, object> param, string link, SipayTokenModel token = null)
        {
            string responseString = "";

            try
            {
                ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                System.Net.ServicePointManager.Expect100Continue = false;

                string jsonContent = JsonConvertHelper.Json(param);

                using (HttpClient client = new HttpClient())
                using (var req = new StringContent(jsonContent, Encoding.UTF8, "application/json"))
                {
                    req.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.WebName };

                    if (token != null)
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.token);

                    var response = client.PostAsync(link, req).Result;
                    byte[] responseByte = response.Content.ReadAsByteArrayAsync().Result;
                    responseString = Encoding.UTF8.GetString(responseByte);
                }
            }
            catch { }

            return responseString;
        }

        //private string GenerateHashKey(string total, string installment, string currencyCode, string merchantKey, string invoiceId, string appSecret)
        private string GenerateHashKey(string data, string appSecret)
        {

            var mtRand = new Random();

            var iv = Sha1Hash(mtRand.Next().ToString()).Substring(0, 16);
            var password = Sha1Hash(appSecret);
            var salt = Sha1Hash(mtRand.Next().ToString()).Substring(0, 4);

            var saltWithPassword = "";
            using (var sha256Hash = SHA256.Create())
            {
                saltWithPassword = GetHash(sha256Hash, password + salt);
            }

            var encrypted = Encryptor(data, saltWithPassword.Substring(0, 32), iv);

            var msgEncryptedBundle = iv + ":" + salt + ":" + encrypted;
            msgEncryptedBundle = msgEncryptedBundle.Replace("/", "__");

            return msgEncryptedBundle;
        }

        private IList<string> ValidateHashKey(string hashKey, string appSecret)
        {
            hashKey = hashKey.Replace("__", "/");

            var password = Sha1Hash(appSecret);

            IList<string> mainStringArray = hashKey.Split(':').ToList();

            if (mainStringArray.Count == 3)
            {
                var iv = mainStringArray[0];
                var salt = mainStringArray[1];
                var mainKey = mainStringArray[2];

                var saltWithPassword = "";
                using (var sha256Hash = SHA256.Create())
                {
                    saltWithPassword = GetHash(sha256Hash, password + salt);
                }

                var orginalValues = Decryptor(mainKey, saltWithPassword.Substring(0, 32), iv);
                IList<string> valueList = orginalValues.Split('|').ToList();

                return valueList;
            }

            return new List<string>();
        }

        private string GetHash(HashAlgorithm hashAlgorithm, string input)
        {
            var data = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sBuilder = new StringBuilder();
            for (var i = 0; i < data.Length; i++) sBuilder.Append(data[i].ToString("x2"));

            return sBuilder.ToString();
        }

        private string Sha1Hash(string password)
        {
            return string.Join("",
                SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(password))
                    .Select(x => x.ToString("x2")));
        }

        private string Encryptor(string textToEncrypt, string strKey, string strIV)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(textToEncrypt);

            using (var aesProvider = Aes.Create())
            {
                aesProvider.BlockSize = 128;
                aesProvider.KeySize = 256;
                aesProvider.Key = Encoding.UTF8.GetBytes(strKey);
                aesProvider.IV = Encoding.UTF8.GetBytes(strIV);
                aesProvider.Padding = PaddingMode.PKCS7;
                aesProvider.Mode = CipherMode.CBC;

                var cryptoTransform = aesProvider.CreateEncryptor(aesProvider.Key, aesProvider.IV);
                var encryptedBytes = cryptoTransform.TransformFinalBlock(plainTextBytes, 0, plainTextBytes.Length);

                return Convert.ToBase64String(encryptedBytes);
            }
        }

        private string Decryptor(string textToDecrypt, string strKey, string strIv)
        {
            var encryptedBytes = Convert.FromBase64String(textToDecrypt);

            using (var aesProvider = Aes.Create())
            {
                aesProvider.BlockSize = 128;
                aesProvider.KeySize = 256;
                aesProvider.Key = Encoding.ASCII.GetBytes(strKey);
                aesProvider.IV = Encoding.ASCII.GetBytes(strIv);
                aesProvider.Padding = PaddingMode.PKCS7;
                aesProvider.Mode = CipherMode.CBC;

                var cryptoTransform = aesProvider.CreateDecryptor(aesProvider.Key, aesProvider.IV);
                var decryptedBytes = cryptoTransform.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                return Encoding.ASCII.GetString(decryptedBytes);
            }
        }
    }



    internal class Sipay3DCompleteModel
    {
        public int status_code { get; set; }
        public string status_description { get; set; }
        public Dictionary<string, object> data { get; set; }
    }


    internal class SipayTokenModel
    {
        public string token { get; set; }
        public Token_is_3d is_3d { get; set; }
        public string expires_at { get; set; }
    }
    internal enum Token_is_3d
    {
        _2DOnly = 0,
        _2Dor3D = 1,
        _3DOnly = 2,
        _Brand = 4
    }
}
