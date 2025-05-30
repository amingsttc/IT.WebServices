﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IT.WebServices.Fragments.Authorization.Payment.Stripe;

namespace IT.WebServices.Authorization.Payment.Stripe.Data
{
    public interface IOneTimeRecordProvider
    {
        Task Save(StripeOneTimePaymentRecord record);
        Task<bool> Exists(Guid userId, Guid recordId);
        IAsyncEnumerable<StripeOneTimePaymentRecord> GetAll();
        Task<StripeOneTimePaymentRecord?> GetById(Guid userId, Guid recordId);
        Task<List<StripeOneTimePaymentRecord>> GetAllByUserId(Guid userId);
    }
}
