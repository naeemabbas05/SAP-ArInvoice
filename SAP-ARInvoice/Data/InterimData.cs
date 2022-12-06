using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SAP_ARInvoice.Model.Setting;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using SAPbobsCOM;
using SAP_ARInvoice.Connection;

namespace SAP_ARInvoice.Data
{
    public class InterimData
    {

        private Setting _setting;
        public InterimData(IOptions<Setting> setting)
        {
            _setting = setting.Value;
        }

        public void ArInvoice_SP(string SpName)
        {
            try
            {
                //Store the connection string in the ConnectionString variable
                string ConnectionString = _setting.DbConnection;
                //Create the SqlConnection object
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    //Create the SqlCommand object by passing the stored procedure name and connection object as parameters
                    SqlCommand cmd = new SqlCommand(SpName, connection)
                    {
                        //Specify the command type as Stored Procedure
                        CommandType = CommandType.StoredProcedure
                    };
                    //Open the Connection
                    connection.Open();
                    //Execute the command i.e. Executing the Stored Procedure using ExecuteReader method
                    //SqlDataReader requires an active and open connection
                    SqlDataReader sdr = cmd.ExecuteReader();
                    //Read the data from the SqlDataReader 
                    //Read() method will returns true as long as data is there in the SqlDataReader
                    while (sdr.Read())
                    {
                        //Accessing the data using the string key as index
                        Console.WriteLine(sdr["Id"] + ",  " + sdr["Name"] + ",  " + sdr["Email"] + ",  " + sdr["Mobile"]);
                        //Accessing the data using the integer index position as key
                        //Console.WriteLine(sdr[0] + ",  " + sdr[1] + ",  " + sdr[2] + ",  " + sdr[3]);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception Occurred: {ex.Message}");
            }
            Console.ReadKey();
        }

        public void ArInvoice_API()
        {
            HttpClient client = new HttpClient();

            client.BaseAddress = new Uri("https://api.nationalize.io/");

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = client.GetAsync("?name=nathaniel").Result;
            if (response.IsSuccessStatusCode)
            {
                var data = response.Content.ReadAsStringAsync().Result;

                CountryData jsonResponse = JsonConvert.DeserializeObject<CountryData>(data);
                var connection = new SAP_Connection();
                if (connection.Connect() == 0)
                {
                    Recordset oRecordSet;
                    oRecordSet = connection.GetCompany().GetBusinessObject(BoObjectTypes.BoRecordset);

                    foreach (var item in jsonResponse.country)
                    {
                        SAPbobsCOM.Items items = connection.GetCompany().GetBusinessObject(BoObjectTypes.oItems);
                        items.ItemCode = item.country_id.ToString();
                        items.ItemName = item.probability.ToString();
                        items.Add();

                    }
                }
            }

            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }
        }
    }

}

