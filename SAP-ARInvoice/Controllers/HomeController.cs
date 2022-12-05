using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System;

namespace SAP_ARInvoice.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HomeController
    {
        [HttpGet]
        public string Get()
        {
            return "SAP B1 Background service";
        }
    }
}
