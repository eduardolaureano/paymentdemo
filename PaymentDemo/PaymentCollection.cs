// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Twilio;

namespace VSSample
{

    public static class PaymentCollection
    {
        private static int SmsInterval = Int32.Parse(Environment.GetEnvironmentVariable("SmsInterval"));

        [FunctionName("SmsPaymentCollection")]
        public static async Task<String> Run(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            string phoneNumber = context.GetInput<string>();
            // Validate phone number
            if (string.IsNullOrEmpty(phoneNumber))
            {
                throw new ArgumentNullException(nameof(phoneNumber),
                        "A phone number input is required.");
            }

            using (var timeoutCts = new CancellationTokenSource())
            {
                // The customer has some time to respond with the code they received in the SMS message.
                bool thankyou = false;
                DateTime expiration = context.CurrentUtcDateTime.AddSeconds(SmsInterval);
                Task timeoutTask = context.CreateTimer(expiration, timeoutCts.Token);
                string SmsBody = "Hello, you have a bill due. Please pay your bill to stop receiving these messages. Clik here <ADD LINK> when you're done paying";
                string smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(phoneNumber, SmsBody));
                Task<String> paymentResponseTask = context.WaitForExternalEvent<String>("PaymentResponse");
                Task winner = await Task.WhenAny(paymentResponseTask, timeoutTask);

                // check if payment was received or timeout expired #1 
                if (winner == timeoutTask)
                {
                    expiration = context.CurrentUtcDateTime.AddSeconds(SmsInterval);
                    timeoutTask = context.CreateTimer(expiration, timeoutCts.Token);
                    SmsBody = "Hi again, you still haven't paid your bill. Please send in your payment to stop receiving messages";
                    smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(phoneNumber, SmsBody));
                    paymentResponseTask = context.WaitForExternalEvent<String>("PaymentResponse");
                    winner = await Task.WhenAny(paymentResponseTask, timeoutTask);

                    // check if payment was received or timeout expired #3
                    if (winner == timeoutTask)
                    {
                        expiration = context.CurrentUtcDateTime.AddSeconds(SmsInterval);
                        timeoutTask = context.CreateTimer(expiration, timeoutCts.Token);
                        SmsBody = "Hi one more time, this is your FINAL reminder. Please send in your payment to stop receiving messages";
                        smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(phoneNumber, SmsBody));
                        paymentResponseTask = context.WaitForExternalEvent<String>("PaymentResponse");
                        winner = await Task.WhenAny(paymentResponseTask, timeoutTask);
                    }
                    else if (winner != timeoutTask)
                    {
                        thankyou = true;
                    }
                }
                else
                {
                    thankyou = true;
                }

                if (thankyou)
                {
                    SmsBody = "Thank you for your payment!";
                    smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(phoneNumber, SmsBody));
                }

                string resultStr = "Payment pending";
                if (!timeoutTask.IsCompleted)
                {
                    // All pending timers must be complete or canceled before the function exits.
                    timeoutCts.Cancel();
                    resultStr = "Payment completed";
                }
                return resultStr;
            }
        }


        [FunctionName("SendSmsReminder")]
        public static String SendSmsReminder(
            [ActivityTrigger] SmsDetails inputs,
            TraceWriter log,
            [TwilioSms(AccountSidSetting = "TwilioAccountSid",
                       AuthTokenSetting = "TwilioAuthToken",
                       From = "%TwilioPhoneNumber%")] out SMSMessage message)
        {
            // Get a random number generator with a random seed (not time-based)
            log.Info($"Sending reminder message to {inputs.PhoneNr}.");

            message = new SMSMessage { To = inputs.PhoneNr };
            message.Body = inputs.TxtBody;

            return message.Body;
        }
    }
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
