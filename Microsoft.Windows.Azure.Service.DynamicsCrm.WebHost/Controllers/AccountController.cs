﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Microsoft.WindowsAzure.Mobile.Service;
using Microsoft.WindowsAzure.Mobile.Service.Security;
using System.Threading.Tasks;
using Microsoft.Windows.Azure.Service.DynamicsCrm.WebHost.Models;
using System.Web.Http.OData.Query;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Client.Services;

namespace Microsoft.Windows.Azure.Service.DynamicsCrm.WebHost.Controllers
{
    [AuthorizeLevel(AuthorizationLevel.User)]
    public class AccountController : TableController<AccountDto>
    {
        protected override void Initialize(System.Web.Http.Controllers.HttpControllerContext controllerContext)
        {
            base.Initialize(controllerContext);

            var orgService = new OrganizationService("CrmConnection");
            DomainManager = new DynamicsCrmDomainManager<AccountDto, Account>(orgService);
        }

        // GET api/Account
        public Task<IEnumerable<AccountDto>> Get(ODataQueryOptions<AccountDto> query)
        {
            return DomainManager.QueryAsync(query);
        }

    }
}
