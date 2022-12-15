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
using SAP_ARInvoice.Model.DTO;

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
                IDictionary<string, string> parameters = new Dictionary<string, string>();
                parameters.Add("@Date", DateTime.Now.ToString("yyyy/MM/dd"));
                List<Orders> invoices = await InvoiceMapper(await connection.ArInvoice_SP<DataModel>("[dbo].[SP_AR_Invoice]", parameters));
                foreach (var singleInvoice in invoices) {
                    var userResponse = await CheckBussinessCustomer(singleInvoice.CustName);
                    if (!userResponse)
                    {
                        _logger.LogError("Unable to Create New User");
                        return "SAP B1 Background service";
                    }

                    var productResponse = await CheckItemExist(singleInvoice.OrderDetail);
                    if (!productResponse)
                    {
                        _logger.LogError("Unable to Create New Item");
                        return "SAP B1 Background service";
                    }


                    invoice = connection.GetCompany().GetBusinessObject(BoObjectTypes.oInvoices);

                    invoice.CardCode = singleInvoice.CustName;
                    invoice.DocDueDate = DateTime.Now;
                    invoice.DocDate = DateTime.Now;
                    //UDF Invoice 
                    //invoice.UserFields.Fields.Item("bill_no").Value = "";

                    foreach (var OrderItem in singleInvoice.OrderDetail) {

                        invoice.Lines.ItemCode = OrderItem.ItemCode;
                        invoice.Lines.ItemDescription = OrderItem.ItemCode;
                        //invoice.Lines.WarehouseCode = "05";
                        invoice.Lines.Quantity = OrderItem.Quantity;
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

                        recordSet.DoQuery($"SELECT \"Code\" FROM \"ITT1\" WHERE \"Father\"='{OrderItem.ItemCode}'");
                        if (recordSet.RecordCount != 0)
                        {
                            while (!recordSet.EoF)
                            {
                                var itemCode = recordSet.Fields.Item(0).Value.ToString();
                                recordSetOBTN.DoQuery($"SELECT \"ExpDate\",\"Quantity\",\"DistNumber\" FROM \"OBTN\" WHERE \"ItemCode\"='{itemCode}'  Order By \"ExpDate\"");
                                var CurrentQuantity = OrderItem.Quantity;
                                var TotalCount = recordSetOBTN.RecordCount;
                                var CurrentCount = 0;

                                while (TotalCount > CurrentCount)
                                {
                                    if (CurrentQuantity > 0) {
                                        var ExpDate = recordSetOBTN.Fields.Item(0).Value.ToString();
                                        var AvailableQuantity = recordSetOBTN.Fields.Item(1).Value.ToString();
                                        var BatchNumber = recordSetOBTN.Fields.Item(2).Value.ToString();
                                        if (int.Parse(AvailableQuantity) > 0) {
                                            invoice.Lines.BatchNumbers.BatchNumber = BatchNumber;
                                            invoice.Lines.BatchNumbers.ItemCode = itemCode;
                                            invoice.Lines.BatchNumbers.ExpiryDate = ExpDate;

                                            if (AvailableQuantity >= CurrentQuantity)
                                            {
                                                invoice.Lines.BatchNumbers.Quantity = CurrentQuantity;
                                                CurrentQuantity = 0;
                                            }
                                            else
                                            {
                                                invoice.Lines.BatchNumbers.Quantity = AvailableQuantity;
                                                CurrentQuantity = CurrentQuantity - AvailableQuantity;
                                            }
                                            invoice.Lines.BatchNumbers.Add();
                                        };
                                       
                                    }
                                    CurrentCount += 1;
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
                            _logger.LogError($"No BOM found angainst given Item");
                            return "SAP B1 Background service";
                        }
                        #endregion


                        invoice.Lines.Add();
                    }

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
            }
            else
            {
                Console.WriteLine("Error " + connection.GetErrorCode() + ": " + connection.GetErrorMessage());
            }
            return "SAP B1 Background service";
        }


        private async Task<List<Orders>> InvoiceMapper(List<DataModel> data) {

            List<Orders> orders = new List<Orders>();
            List<DataModel> resp = data.Select(x=> new {x.CustName,x.OrderCode}).Distinct().Select(x => data.FirstOrDefault(r => r.CustName == x.CustName && r.OrderCode==x.OrderCode)).Distinct().ToList();
            foreach (var item in resp)
            {
               var orderDetail = data.Where(x => x.OrderCode == item.OrderCode && x.CustName == item.CustName).Select(x => new OrderDetail { ItemCode=x.ItemCode,Quantity=int.Parse(x.Quantity) }).Distinct().ToList();
                orders.Add(new Orders() { CustName= item.CustName,OrderCode=item.OrderCode,OrderDate=item.OrderDate,OrderDetail= orderDetail });
            }

            return orders;
        }

        private async Task<bool> CheckItemExist(List<OrderDetail> orderDetail)
        {
            bool output = false;
            SAPbobsCOM.Items product = null;
            SAPbobsCOM.Recordset recordSet = null;
            recordSet = connection.GetCompany().GetBusinessObject(BoObjectTypes.BoRecordset);
            product = connection.GetCompany().GetBusinessObject(BoObjectTypes.oItems);

            foreach (var singleOrderDetail in orderDetail) {
                recordSet.DoQuery($"SELECT * FROM \"OITM\" WHERE \"ItemCode\"='{singleOrderDetail.ItemCode}'");
                if (recordSet.RecordCount == 0)
                {
                    IDictionary<string, string> parameters = new Dictionary<string, string>();
                    parameters.Add("@ItemCode", singleOrderDetail.ItemCode);
                    List<Item> items = await connection.ArInvoice_SP<Item>("GetItems", parameters);
                    foreach (var item in items)
                    {
                        product.ItemCode = item.ItemCode;
                        product.ItemName = item.ItemDescription;
                        //need to check
                        //product. = Double.Parse(item.UnitPrice);

                        var resp = product.Add();
                        if (resp.Equals(0))
                        {
                            output = true;
                        }
                        else
                        {
                            output = false;
                        }

                    }

                }
                else
                {
                    output = true;
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
                IDictionary<string, string> parameters = new Dictionary<string, string>();
                parameters.Add("@CardCode", CustomerId);

                List<Customer> customer = await connection.ArInvoice_SP<Customer>("[dbo].[GetCustomer]", parameters);
                foreach (var item in customer) {
                    businessPartners.CardCode = item.CardCode;
                    businessPartners.CardName = item.CustName;
                    businessPartners.Phone1 = item.Phone;
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
