﻿using System;
using System.IO;
using System.Linq;
using static System.Console;

namespace LinqToSalesforce.Example1
{
    class Program
    {
        static void Main(string[] args)
        {
            var json = File.ReadAllText("../../../../src/Files/OAuth.config.json");
            var impersonationParam = Rest.OAuth.ImpersonationParam.FromJson(json);

            var context = new SoqlContext("eu11", impersonationParam);
            try
            {
                RenameAccountsStartingWithCompany(context);

                DisplayAccountsWithTheirContactsAndCases(context);
            }
            catch (AggregateException ex)
            {
                WriteLine($"Error: {ex.Message}");
                foreach (var e in ex.InnerExceptions)
                    WriteLine($"Error: {e.Message}");
            }
            catch (Exception ex)
            {
                WriteLine($"Error: {ex.Message}");
            }

            WriteLine("Press any key to continue ...");
            ReadKey(true);
        }

        static void RenameAccountsStartingWithCompany(SoqlContext context)
        {
            var accounts = from a in context.GetTable<Account>()
                           where a.Name.StartsWith("Company")
                           select a;

            foreach (var account in accounts)
            {
                var newName = $"{account.Name}_{DateTime.Now.Ticks}";
                WriteLine($"Account {account.Name} renamed to {newName}");
                account.Name = newName;
            }

            context.Save();
        }

        private static void DisplayAccountsWithTheirContactsAndCases(SoqlContext context)
        {
            var accounts = (from a in context.GetTable<Account>()
                            where !a.Name.StartsWith("Company")
                                && a.Industry == PickAccountIndustry.Biotechnology
                            select a)
                //.Skip(1) // not implemented on all REST API versions
                .Take(10)
                .ToList();

            foreach (var account in accounts)
            {
                WriteLine($"Account {account.Name} Industry: {account.Industry}");
                var contacts = account.Contacts.ToList();
                foreach (var contact in contacts)
                {
                    WriteLine($"contact: {contact.Name} - {contact.Phone} - {contact.LeadSource}");

                    var cases = contact.Cases;
                    foreach (var @case in cases)
                    {
                        WriteLine($"case: {@case.Id}");
                    }
                }
            }
        }
    }
}