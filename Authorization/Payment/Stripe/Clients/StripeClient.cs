﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IT.WebServices.Authorization.Payment.Stripe.Data;
using IT.WebServices.Fragments.Authorization;
using IT.WebServices.Fragments.Authorization.Payment.Stripe;
using Stripe;
using IT.WebServices.Models;
using IT.WebServices.Authentication;
using IT.WebServices.Settings;

namespace IT.WebServices.Authorization.Payment.Stripe.Clients
{
    public class StripeClient
    {
        public const string PRODUCT_SUBSCRIPTION_PREFIX = "prod_sub_";
        public const string PRODUCT_ONETIME_PREFIX = "prod_one_";

        public ProductList Products { get; private set; }

        private readonly AppSettings settings;
        private readonly IProductRecordProvider recordProvider;
        private readonly ILogger<StripeClient> logger;
        private readonly SettingsClient settingsClient;

        private global::Stripe.Checkout.SessionService checkoutService = new();
        private CustomerService customerService = new();
        private PaymentIntentService paymentService = new();
        private ProductService productService = new();
        private PriceService priceService = new();
        private SubscriptionService subService = new();

        private object syncObject = new();

        public StripeClient(
            ILogger<StripeClient> logger,
            IOptions<AppSettings> settings,
            IProductRecordProvider recordProvider,
            SettingsClient settingsClient
        )
        {
            this.settings = settings.Value;
            this.logger = logger;
            this.settingsClient = settingsClient;
            this.recordProvider = recordProvider;

            // Set Client Secret
            StripeConfiguration.ApiKey = settingsClient.OwnerData.Subscription.Stripe.ClientSecret;

            Products = recordProvider.GetAll().Result;
            EnsureProducts();
        }

        public async Task<Product?> EnsureOneTimeProduct(StripeEnsureOneTimeProductRequest request)
        {
            try
            {
                var product = await GetProduct(request);
                if (product == null)
                    return await CreateOneTimeProduct(request);

                if (product.Active != true)
                    return await ModifyOneTimeProduct(request, product);

                if (product.Name != request.Name)
                    return await ModifyOneTimeProduct(request, product);

                return product;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return null;
        }

        public async Task<StripeEnsureOneTimeProductResponse> EnsureOneTimeProductDefaultPrice(Product product, Price price)
        {
            try
            {
                if (product.DefaultPriceId == price.Id)
                    return new();

                var modifyProductOpts = new ProductUpdateOptions { DefaultPrice = price.Id };
                var updated = await productService.UpdateAsync(product.Id, modifyProductOpts);

                return new();
            }
            catch (Exception ex)
            {
                return new() { Error = ex.Message, };
            }
        }

        private Task<Product?> GetProduct(StripeEnsureOneTimeProductRequest request) => GetProduct(request.InternalID);

        private async Task<Product?> GetProduct(string internalId)
        {
            try
            {
                return await productService.GetAsync(PRODUCT_ONETIME_PREFIX + internalId);
            }
            catch
            {
                return null;
            }
        }

        private async Task<Product?> CreateOneTimeProduct(StripeEnsureOneTimeProductRequest request)
        {
            try
            {
                var newProductOpts = new ProductCreateOptions()
                {
                    Id = PRODUCT_ONETIME_PREFIX + request.InternalID,
                    Active = true,
                    Name = request.Name,
                    Description = request.Name,
                };

                return await productService.CreateAsync(newProductOpts);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        private async Task<Product?> ModifyOneTimeProduct(StripeEnsureOneTimeProductRequest request, Product product)
        {
            try
            {
                var modifyProductOpts = new ProductUpdateOptions { Name = request.Name, Active = true };

                return await productService.UpdateAsync(product.Id, modifyProductOpts);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        public async Task<Price?> EnsureOneTimePrice(StripeEnsureOneTimeProductRequest request, Product product)
        {
            try
            {
                var price = await priceService.GetAsync(product.DefaultPriceId);

                if (price == null)
                    return await CreateOneTimePrice(request);

                if (price.Active != true)
                    return await CreateOneTimePrice(request);

                if (price.CustomUnitAmount == null)
                    return await CreateOneTimePrice(request);

                if (price.CustomUnitAmount.Minimum != request.MinimumPrice)
                    return await CreateOneTimePrice(request);

                if (price.CustomUnitAmount.Preset != null)
                    return await CreateOneTimePrice(request);

                if (price.CustomUnitAmount.Maximum != request.MaximumPrice)
                    return await CreateOneTimePrice(request);

                return price;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        private async Task<Price?> CreateOneTimePrice(StripeEnsureOneTimeProductRequest request)
        {
            try
            {
                var newPriceOpts = new PriceCreateOptions()
                {
                    Currency = "usd",
                    Active = true,
                    Metadata = new()
                    {
                        { "internal_id", request.InternalID },
                    },
                    Nickname = request.Name,
                    Product = PRODUCT_ONETIME_PREFIX + request.InternalID,
                    CustomUnitAmount = new PriceCustomUnitAmountOptions()
                    {
                        Enabled = true,
                        Minimum = request.MinimumPrice,
                        //Preset = request.MinimumPrice,
                        Maximum = request.MaximumPrice,
                    }
                };

                return await priceService.CreateAsync(newPriceOpts);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        private async Task<Price?> CreateOneTimePriceInternal(string internalId, string name, uint minimum, uint preset, uint maximum)
        {
            var newPriceOpts = new PriceCreateOptions()
            {
                Currency = "usd",
                Active = true,
                Metadata = new()
                    {
                        { "internal_id", internalId },
                    },
                Nickname = name,
                Product = PRODUCT_ONETIME_PREFIX + internalId,
                CustomUnitAmount = new PriceCustomUnitAmountOptions()
                {
                    Enabled = true,
                    Minimum = minimum,
                    Preset = preset,
                    Maximum = maximum,
                }
            };

            var createdPrice = await priceService.CreateAsync(newPriceOpts);
            if (createdPrice == null)
                throw new Exception("Failed To Create Price");

            return createdPrice;
        }

        public async Task<StripeNewDetails?> GetNewDetails(uint level, ONUser userToken, string domainName)
        {
            var product = Products.Records.FirstOrDefault(r => r.Price == level);
            if (product == null)
                return null;

            var url = await CreateCheckoutSession(product, userToken, domainName);
            if (url == null)
                return null;

            var details = new StripeNewDetails() { PaymentLink = url, };

            return details;
        }

        public async Task<StripeNewOneTimeDetails?> GetNewOneTimeDetails(string internalId, ONUser userToken, string domainName, uint differentPresetPriceCents)
        {
            try
            {
                var product = await GetProduct(internalId);
                if (product == null)
                    return null;

                var priceId = product.DefaultPriceId;

                if (differentPresetPriceCents > 0)
                {
                    try
                    {
                        var price = await priceService.GetAsync(priceId);
                        uint minimum = (uint)(price.CustomUnitAmount.Minimum ?? 0);
                        uint maximum = (uint)(price.CustomUnitAmount.Maximum ?? 0);
                        if (differentPresetPriceCents > minimum && differentPresetPriceCents <= maximum)
                        {
                            var newPrice = await CreateOneTimePriceInternal(internalId, "custom", minimum, differentPresetPriceCents, maximum);
                            if (newPrice != null)
                                priceId = newPrice.Id;
                        }
                    }
                    catch { }
                }

                var url = await CreateOneTimeCheckoutSession(priceId, internalId, userToken, domainName);
                if (string.IsNullOrEmpty(url))
                    return null;

                var details = new StripeNewOneTimeDetails() { PaymentLink = url, };

                return details;
            }
            catch (Exception ex)
            {
                logger.LogError($"Stripe Client: {ex.Message}", ex);
                return null;
            }
        }

        public async Task<string?> CreateOneTimeCheckoutSession(string priceId, string contentId, ONUser userToken, string domainName)
        {
            try
            {
                var customer = await EnsureCustomerByUserId(userToken.Id);
                if (customer == null)
                    return null;
                var chekoutOpts = new global::Stripe.Checkout.SessionCreateOptions
                {
                    ClientReferenceId = userToken.Id.ToString(),
                    SuccessUrl = domainName + "/payment/stripe/check",
                    CancelUrl = domainName,
                    Mode = "payment",
                    LineItems = new()
                    {
                        new() { Price = priceId, Quantity = 1, },
                    },
                    Customer = customer.Id
                };

                var session = await checkoutService.CreateAsync(chekoutOpts);

                return session.Url;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public async Task<string?> CreateCheckoutSession(ProductRecord product, ONUser userToken, string domainName)
        {
            try
            {
                var customer = await EnsureCustomerByUserId(userToken.Id);
                if (customer == null)
                    return null;

                var chekoutOpts = new global::Stripe.Checkout.SessionCreateOptions
                {
                    ClientReferenceId = userToken.Id.ToString(),
                    SuccessUrl = domainName + "/subscription/stripe/check",
                    CancelUrl = domainName + "/subscription/",
                    Mode = "subscription",
                    LineItems = new()
                    {
                        new() { Price = product.PriceID, Quantity = 1, },
                    },
                    Customer = customer.Id
                };

                var session = await checkoutService.CreateAsync(chekoutOpts);

                return session.Url;
            }
            catch
            {
                return null;
            }
        }

        public async Task<Customer?> EnsureCustomerByUserId(Guid userId)
        {
            try
            {
                var customer = await GetCustomerByUserId(userId);
                if (customer != null)
                    return customer;

                var dict = new Dictionary<string, string>();
                dict["id"] = userId.ToString();

                return await customerService.CreateAsync(new() { Metadata = dict });
            }
            catch { }

            return null;
        }

        public async Task<Customer?> GetCustomerByUserId(Guid userId)
        {
            try
            {
                var res = await customerService.SearchAsync(
                    new() { Query = $"metadata['id']:'{userId.ToString()}'" }
                );
                return res.FirstOrDefault();
            }
            catch { }

            return null;
        }

        private string CreatePaymentLink(string priceId)
        {
            var opts = new PaymentLinkCreateOptions()
            {
                LineItems = new List<PaymentLinkLineItemOptions>
                {
                    new PaymentLinkLineItemOptions { Price = priceId, Quantity = 1, },
                },
            };

            var service = new PaymentLinkService();
            var link = service.Create(opts);
            return link.Url.ToString();
        }

        // TODO: Validate metadata has key url
        private void EnsureProducts()
        {
            var ptest = productService.List();
            var stripeProducts = productService.List().Where(p => p.Id.StartsWith(PRODUCT_SUBSCRIPTION_PREFIX)).ToList();
            var stripePrices = priceService.List().Where(p => p.ProductId.StartsWith(PRODUCT_SUBSCRIPTION_PREFIX)).ToList();
            logger.LogWarning($"****PRODS: {stripeProducts}");

            var tiers = settingsClient.PublicData.Subscription.Tiers.ToList();
            List<ProductRecord> goodList = new();
            foreach (var t in tiers)
            {
                var product = EnsureProduct(t, stripeProducts, stripePrices);
                goodList.Add(product);
            }

            Products.Records.Clear();
            Products.Records.AddRange(goodList);

            recordProvider.SaveAll(Products).Wait();
        }

        private ProductRecord EnsureProduct(
            SubscriptionTier t,
            List<Product> stripeProducts,
            List<Price> stripePrices
        )
        {
            var savedProduct = Products.Records.FirstOrDefault(r => r.Price == t.AmountCents);
            if (savedProduct != null)
            {
                bool alreadyCorrect = IsSavedRecordIsCorrect(
                    t,
                    savedProduct,
                    stripeProducts,
                    stripePrices
                );
                if (alreadyCorrect)
                    return savedProduct;
            }

            var activeProduct = stripeProducts
                .Where(p => p.Active)
                .Where(p => p.Name.ToLower() == t.Name.ToLower())
                .FirstOrDefault();
            if (activeProduct == null)
            {
                var productInactive = stripeProducts
                    .Where(p => p.Name.ToLower() == t.Name.ToLower())
                    .FirstOrDefault();
                if (productInactive != null)
                {
                    activeProduct = productService.Update(
                        productInactive.Id,
                        new() { Active = true }
                    );
                }
                else
                {
                    activeProduct = productService.Create(
                        new()
                        {
                            Id = PRODUCT_SUBSCRIPTION_PREFIX + Guid.NewGuid().ToString(),
                            Active = true,
                            Name = t.Name,
                            Description = t.Description
                        }
                    );
                }
            }

            var price = EnsurePrice(t, activeProduct, stripePrices);

            var url = CreatePaymentLink(price.Id);

            return new ProductRecord
            {
                Name = t.Name,
                Price = (int)t.AmountCents,
                PriceID = price.Id,
                ProductID = activeProduct.Id,
                CheckoutUrl = url,
            };
        }

        private bool IsSavedRecordIsCorrect(
            SubscriptionTier t,
            ProductRecord savedProduct,
            List<Product> stripeProducts,
            List<Price> stripePrices
        )
        {
            if (savedProduct == null)
                return false;

            if (t.AmountCents != savedProduct.Price)
                return false;
            if (t.Name != savedProduct.Name)
                return false;

            var product = stripeProducts.FirstOrDefault(p => p.Id == savedProduct.ProductID);
            if (product == null)
                return false;
            bool productCorrect = IsProductCorrect(t, product);
            if (!productCorrect)
                return false;

            var price = stripePrices.FirstOrDefault(p => p.Id == savedProduct.PriceID);
            if (price == null)
                return false;
            bool priceCorrect = IsPriceCorrect(t, product, price);
            if (!priceCorrect)
                return false;

            return true;
        }

        private bool IsProductCorrect(SubscriptionTier t, Product product)
        {
            if (!product.Active)
                return false;
            if (product.Name.ToLower() != t.Name.ToLower())
                return false;

            return true;
        }

        private bool IsPriceCorrect(SubscriptionTier t, Product product, Price price)
        {
            if (!price.Active)
                return false;
            if (price.ProductId != product.Id)
                return false;
            if (price.Currency != "usd")
                return false;
            if (price.UnitAmount != t.AmountCents)
                return false;

            return true;
        }

        private Price EnsurePrice(
            SubscriptionTier t,
            Product product,
            List<Price> stripePrices
        )
        {
            foreach (Price price in stripePrices.Where(p => p.Active))
            {
                if (IsPriceCorrect(t, product, price))
                    return price;
            }

            var newPrice = priceService.Create(
                new()
                {
                    Active = true,
                    Currency = "usd",
                    UnitAmount = t.AmountCents,
                    Nickname = t.Name,
                    Product = product.Id,
                    Recurring = new() { Interval = "month", }
                }
            );

            return newPrice;
        }

        public async Task<Subscription> GetSubscription(string id)
        {
            try
            {
                var sub = await subService.GetAsync(id, new());
                return sub;
            }
            catch { }

            return new();
        }

        public async Task<List<Subscription>> GetSubscriptionsByCustomerId(string id)
        {
            try
            {
                var customer = await customerService.GetAsync(
                    id,
                    new() { Expand = new() { "subscriptions" } }
                );
                return customer.Subscriptions.ToList();
            }
            catch { }

            return new();
        }

        public async Task<List<PaymentIntent>> GetOneTimePaymentsByCustomerId(string id)
        {
            try
            {
                var payments = await paymentService.ListAsync(
                    new() { Customer = id }
                );

                return payments.ToList();
            }
            catch { }

            return new();
        }

        internal async Task<bool> CancelSubscription(string id, string reason)
        {
            try
            {
                var sub = await subService.CancelAsync(
                    id,
                    new() { CancellationDetails = new() { Comment = reason } }
                );
                return true;
            }
            catch { }

            return false;
        }

        internal async Task<global::Stripe.Checkout.Session?> GetCheckoutSessionByPaymentIntentId(string paymentIntentId)
        {
            try
            {
                var sessions = await checkoutService.ListAsync(new() { PaymentIntent = paymentIntentId, Expand = new() { "data.line_items" } });
                return sessions.FirstOrDefault();
            }
            catch { }

            return null;
        }
    }
}
