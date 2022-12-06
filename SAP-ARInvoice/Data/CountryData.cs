using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SAP_ARInvoice.Data
{
    public class country
    {
        public string country_id { get; set; }
        public string probability { get; set; }

    }
    public class CountryData
    {
        public List<country> country { get; set; }
    }
}
