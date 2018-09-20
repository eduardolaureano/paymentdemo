// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace PaymentDemo
{

    public static class PaymentCollection
    {

        [FunctionName("SmsPaymentCollection")]
        public static async Task<String> SmsPaymentCollection(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            // Pull values sent from UI
            FormData formData = context.GetInput<FormData>();

            // Pull the instanceId and set the payment url
            string instanceId = context.InstanceId;
            string paymentUrl = "https://repoman-ui.azurewebsites.net/submitPayment" + $"/{instanceId}";

            // Validate phone number
            if (string.IsNullOrEmpty(formData.phoneNumber))
            {
                throw new ArgumentNullException(nameof(formData.phoneNumber),
                        "A phone number input is required.");
            }

            using (var timeoutCts = new CancellationTokenSource())
            {
                // The customer has some time to respond with the code they received in the SMS message.
                bool thankyou = false;
                DateTime expiration = context.CurrentUtcDateTime.AddSeconds(Int32.Parse(formData.notificationInterval));
                Task timeoutTask = context.CreateTimer(expiration, timeoutCts.Token);
                string SmsBody = $"Hello, you have a bill due totaling {formData.amountOwed}. Please pay your bill to stop receiving these messages. Click here when you're done paying - {paymentUrl}";
                string smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(formData.phoneNumber, SmsBody));
                Task<String> paymentResponseTask = context.WaitForExternalEvent<String>("PaymentResponse");
                Task winner = await Task.WhenAny(paymentResponseTask, timeoutTask);

                // check if payment was received or timeout expired #1 
                if (winner == timeoutTask)
                {
                    expiration = context.CurrentUtcDateTime.AddSeconds(Int32.Parse(formData.notificationInterval));
                    timeoutTask = context.CreateTimer(expiration, timeoutCts.Token);
                    SmsBody = $"Hi again, you still haven't paid your bill totaling {formData.amountOwed}. Please send in your payment to stop receiving messages - {paymentUrl}";
                    smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(formData.phoneNumber, SmsBody));
                    paymentResponseTask = context.WaitForExternalEvent<String>("PaymentResponse");
                    winner = await Task.WhenAny(paymentResponseTask, timeoutTask);

                    // check if payment was received or timeout expired #3
                    if (winner == timeoutTask)
                    {
                        expiration = context.CurrentUtcDateTime.AddSeconds(Int32.Parse(formData.notificationInterval));
                        timeoutTask = context.CreateTimer(expiration, timeoutCts.Token);
                        SmsBody = $"Hi one more time, this is your FINAL reminder for your bill totaling {formData.amountOwed}. Please send in your payment to stop receiving messages - {paymentUrl}";
                        smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(formData.phoneNumber, SmsBody));
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
                    smsReminder = await context.CallActivityAsync<String>("SendSmsReminder", new SmsDetails(formData.phoneNumber, SmsBody));
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
        [return: TwilioSms(AccountSidSetting = "TwilioAccountSid", AuthTokenSetting = "TwilioAuthToken", From = "%TwilioPhoneNumber%")]
        public static CreateMessageOptions SendSmsReminder(
            [ActivityTrigger] SmsDetails inputs,
            TraceWriter log)
        {
            CreateMessageOptions message = new CreateMessageOptions(new PhoneNumber(inputs.PhoneNr))
            {
                Body = inputs.TxtBody
            };

            return message;
        }
    }
}
