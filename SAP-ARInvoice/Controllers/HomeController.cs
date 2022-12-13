using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System;
using SAPbobsCOM;
using SAP_ARInvoice.Connection;
using SAP_ARInvoice.Model.Setting;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using SAP_ARInvoice.Model;
using Microsoft.Extensions.Logging;

namespace SAP_ARInvoice.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HomeController
    {
        private readonly ILogger _logger;
        private readonly SAP_Connection connection;

        public HomeController(IOptions<Setting> setting, ILogger<HomeController> logger) {
            this.connection = new SAP_Connection(setting.Value);
            _logger = logger;
        }

        [HttpGet]
        public async Task<string> GetAsync()
        {
            
            if (connection.Connect() == 0)
            {
                Documents invoice = null;
                //List<DataModel> invoices = await connection.ArInvoice_SP<DataModel>("--DataModel--");
                //foreach

                var userResponse = await CheckBussinessCustomer("Walking");
                if (!userResponse)
                {
                    _logger.LogError("Unable to Create New User");
                    return "SAP B1 Background service";
                }
                var productResponse = await CheckItemExist("ProductId");

                if (!productResponse) {
                    _logger.LogError("Unable to Create New Item");
                    return "SAP B1 Background service";
                }

                invoice = connection.GetCompany().GetBusinessObject(BoObjectTypes.oInvoices);
               
                invoice.CardCode = "Walking";
                invoice.DocDueDate = DateTime.Now;
                invoice.DocDate = DateTime.Now;

                //UDF Invoice 
                //invoice.UserFields.Fields.Item("bill_no").Value = "";

                invoice.Lines.ItemCode = "FG-000031";
                invoice.Lines.ItemDescription = "QME-FG";
                //invoice.Lines.WarehouseCode = "05";
                invoice.Lines.Quantity = 1;
                invoice.Lines.UnitPrice = 1000;
                //Branch
                //invoice.Lines.COGSCostingCode3 = "";
                //UDF Invoice Lines
                //invoice.Lines.UserFields.Fields.Item("bank").Value = "HBL";
                //invoice.Lines.UserFields.Fields.Item("bank_discount").Value = "00:00";
                //invoice.Lines.UserFields.Fields.Item("tax_code").Value = "";
                //invoice.Lines.UserFields.Fields.Item("tax_amount").Value = "";


                #region Batch wise Item
                SAPbobsCOM.Items product = null;
                SAPbobsCOM.Recordset recordSet = null;
                SAPbobsCOM.Recordset recordSetOBTN = null;
                recordSet = connection.GetCompany().GetBusinessObject(BoObjectTypes.BoRecordset);
                recordSetOBTN = connection.GetCompany().GetBusinessObject(BoObjectTypes.BoRecordset);
                product = connection.GetCompany().GetBusinessObject(BoObjectTypes.oItems);

                recordSet.DoQuery($"SELECT \"ItemCode\" FROM \"OITT\" WHERE \"Code\"='{"ProductId"}'");
                if (recordSet.RecordCount == 0)
                {
                    while (!recordSet.EoF)
                    {
                        var itemCode = recordSet.Fields.Item(0).Value.ToString();
                        recordSetOBTN.DoQuery($"SELECT \"ItemCode\",\"ExpDate\",\"Quantity\",\"DistNumber\" FROM \"OBTN\" WHERE \"ItemCode\"='{"itemCode"}'  Order By \"ExpDate\"");
                        var CurrentQuantity = 10;//Quantity
                        while (!recordSetOBTN.EoF)
                        {
                            if (CurrentQuantity <= 0) continue;

                            var ChildItemCode = recordSet.Fields.Item(0).Value.ToString();
                            var ExpDate = recordSet.Fields.Item(1).Value.ToString();
                            var AvailableQuantity = recordSet.Fields.Item(2).Value.ToString();
                            var BatchNumber = recordSet.Fields.Item(3).Value.ToString();
                            if (AvailableQuantity <= 0) continue;
                            invoice.Lines.BatchNumbers.BatchNumber = BatchNumber;
                            invoice.Lines.BatchNumbers.ItemCode = ChildItemCode;
                            invoice.Lines.BatchNumbers.ExpiryDate = ExpDate;

                            if (AvailableQuantity >= CurrentQuantity)
                            {
                                invoice.Lines.BatchNumbers.Quantity = CurrentQuantity;
                                CurrentQuantity = 0;
                            }
                            else {
                                invoice.Lines.BatchNumbers.Quantity = AvailableQuantity;
                                CurrentQuantity = CurrentQuantity - AvailableQuantity;

                            }
                            invoice.Lines.BatchNumbers.Add();
                        }
                        if (!CurrentQuantity.Equals(0))
                        {
                            _logger.LogError($"Not Enough Data in Given Batch");
                            return "SAP B1 Background service";
                        }


                    }
                }
                else
                {
                    _logger.LogError($"No BOM found angainst given Item {"ProductId"}");
                    return "SAP B1 Background service";
                }
                #endregion


                invoice.Lines.Add();



                if (invoice.Add() == 0)
                {
                    Console.WriteLine("Success:Record added successfully");

                }
                else
                {
                    var errCode = connection.GetCompany().GetLastErrorCode();
                    var response = connection.GetCompany().GetLastErrorDescription();
                    Console.WriteLine("Error:Operation Unsuccessfull");
                }
                connection.GetCompany().Disconnect();
            }
            else
            {
                Console.WriteLine("Error " + connection.GetErrorCode() + ": " + connection.GetErrorMessage());
            }
            return "SAP B1 Background service";
        }

      

        private async Task<bool> CheckItemExist(string ProductId)
        {
            bool output = false;
            SAPbobsCOM.Items product = null;
            SAPbobsCOM.Recordset recordSet = null;
            recordSet = connection.GetCompany().GetBusinessObject(BoObjectTypes.BoRecordset);
            product = connection.GetCompany().GetBusinessObject(BoObjectTypes.oItems);

            recordSet.DoQuery($"SELECT * FROM \"OITM\" WHERE \"ItemCode\"='{ProductId}'");
            if (recordSet.RecordCount == 0)
            {
                List<Item> items = await connection.ArInvoice_SP<Item>("--Item--");
                foreach (var item in items)
                {
                    product.ItemCode = item.ItemCode;
                    product.ItemName = item.ItemName;
                    product.ProdStdCost = item.Price;
                  var resp =  product.Add();
                    if (resp.Equals(0))
                    {
                        output = true;
                    }
                    else {
                        output = false;
                    }

                }
              
            }
            return output;
        }

            private async Task<bool> CheckBussinessCustomer(string CustomerId) {
            bool output = false;
            SAPbobsCOM.Recordset recordSet = null;
            BusinessPartners businessPartners = null;
            recordSet = connection.GetCompany().GetBusinessObject(BoObjectTypes.BoRecordset);
            businessPartners = connection.GetCompany().GetBusinessObject(BoObjectTypes.oBusinessPartners);


            recordSet.DoQuery($"SELECT * FROM \"OCRD\" WHERE \"CardCode\"='{CustomerId}'");
            if (recordSet.RecordCount == 0)
            {
                List<Customer> customer = await connection.ArInvoice_SP<Customer>("--Customer--");
                foreach (var item in customer) {
                    businessPartners.CardCode = item.CardCode;
                    businessPartners.CardName = item.CardName;
                    businessPartners.Phone1 = item.Phone1;
                    businessPartners.CardType = BoCardTypes.cCustomer;
                    businessPartners.SubjectToWithholdingTax = (BoYesNoNoneEnum)BoYesNoEnum.tNO;
                    var response = businessPartners.Add();
                    if (response.Equals(0))
                    {
                        return true;

                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else {
                output = true;
            }
      

            return   output;
        }
    }
}
