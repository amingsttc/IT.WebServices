﻿using Google.Protobuf;
using Microsoft.Extensions.Options;
using IT.WebServices.Fragments.Authorization;
using IT.WebServices.Fragments.Authorization.Payment.Stripe;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IT.WebServices.Models;

namespace IT.WebServices.Authorization.Payment.Stripe.Data
{
    public class FileSystemProductRecordProvider : IProductRecordProvider
    {
        private readonly DirectoryInfo dataDir;
        private readonly FileInfo listFile;

        public FileSystemProductRecordProvider(IOptions<AppSettings> settings)
        {
            var root = new DirectoryInfo(settings.Value.DataStore);
            root.Create();
            dataDir = root.CreateSubdirectory("payment").CreateSubdirectory("stripe").CreateSubdirectory("server");
            listFile = new FileInfo(dataDir.FullName + "/products");
        }

        public async Task<ProductList> GetAll()
        {
            if (!listFile.Exists)
                return new ProductList();

            var bytes = await File.ReadAllBytesAsync(listFile.FullName);

            return ProductList.Parser.ParseFrom(bytes);
        }

        public async Task SaveAll(ProductList list)
        {
            await File.WriteAllBytesAsync(listFile.FullName, list.ToByteArray());
        }
    }
}
