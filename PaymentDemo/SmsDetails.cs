using System;
using System.Collections.Generic;
using System.Text;

namespace PaymentDemo
{
    public class SmsDetails
    {
        public string PhoneNr { get; set; }
        public string TxtBody { get; set; }

        public SmsDetails(string phoneNr, string txtBody)
        {
            PhoneNr = phoneNr;
            TxtBody = txtBody;
        }
    }
}
