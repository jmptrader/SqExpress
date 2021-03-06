﻿using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using NUnit.Framework;
using SqExpress.Syntax;
using SqExpress.Syntax.Boolean;
using SqExpress.Syntax.Boolean.Predicate;
using SqExpress.Syntax.Names;
using SqExpress.Syntax.Value;
using SqExpress.SyntaxTreeOperations;
using static SqExpress.SqQueryBuilder;

namespace SqExpress.Test.Syntax
{
    [TestFixture]
    public class SyntaxTreeOperationsTest
    {
        [Test]
        public void WalkThroughTest()
        {
            var tUser = Tables.User();
            var tCustomer = Tables.Customer();

            var e = Select(tUser.UserId, tUser.FirstName, tCustomer.CustomerId)
                .From(tUser)
                .InnerJoin(tCustomer, on: tCustomer.UserId == tUser.UserId)
                .Done();

            string expected = "0ExprQuerySpecification,1Int32TableColumn,2ExprTableAlias,3ExprAliasGuid," +
                              "2ExprColumnName,1StringTableColumn,2ExprTableAlias,3ExprAliasGuid," +
                              "2ExprColumnName,1Int32TableColumn,2ExprTableAlias,3ExprAliasGuid,2ExprColumnName," +
                              "1ExprJoinedTable,2User,3ExprTableFullName,4ExprDbSchema,5ExprSchemaName," +
                              "4ExprTableName,3ExprTableAlias,4ExprAliasGuid,2Customer,3ExprTableFullName," +
                              "4ExprDbSchema,5ExprSchemaName,4ExprTableName,3ExprTableAlias,4ExprAliasGuid," +
                              "2ExprBooleanEq,3NullableInt32TableColumn,4ExprTableAlias,5ExprAliasGuid," +
                              "4ExprColumnName,3Int32TableColumn,4ExprTableAlias,5ExprAliasGuid,4ExprColumnName,";

            StringBuilder builder = new StringBuilder();

            e.SyntaxTree().WalkThrough((expr, tier) =>
            {
                builder.Append(tier);
                builder.Append(expr.GetType().Name);
                builder.Append(',');
                return VisitorResult<int>.Continue(tier+1);
            }, 0);

            Assert.AreEqual(expected, builder.ToString());

        }

        [Test]
        public void FindTest()
        {
            var tUser = Tables.User();
            var tCustomer = Tables.Customer();

            var e = Select(tUser.UserId, tUser.FirstName, tCustomer.CustomerId)
                .From(tUser)
                .InnerJoin(tCustomer, on: tCustomer.UserId == tUser.UserId)
                .Where(tUser.Version == 5)
                .Done();

            var versionCol =e.SyntaxTree().FirstOrDefault<ExprColumnName>(cn=>cn.Name == tUser.Version.ColumnName.Name);

            Assert.NotNull(versionCol);
            Assert.AreEqual(tUser.Version.ColumnName, versionCol);
        }

        [Test]
        public void ModifyTest()
        {
            var tUser = Tables.User();
            var tCustomer = Tables.Customer();

            IExpr e = Select(tUser.UserId, tUser.FirstName, tCustomer.CustomerId)
                .From(tUser)
                .InnerJoin(tCustomer, on: tCustomer.UserId == tUser.UserId & tUser.Version == 1)
                .Where(tUser.UserId.In(1))
                .Done();

            //Before
            Assert.AreEqual("SELECT [A0].[UserId],[A0].[FirstName],[A1].[CustomerId] " +
                            "FROM [dbo].[user] [A0] JOIN [dbo].[Customer] [A1] " +
                            "ON [A1].[UserId]=[A0].[UserId] AND [A0].[Version]=1 " +
                            "WHERE [A0].[UserId] IN(1)", e.ToSql());

            e = e.SyntaxTree()
                .Modify(subE =>
                    {
                        if (subE is ExprIn)
                        {
                            return null;
                        }
                        if (subE is ExprBooleanAnd and && and.Right is ExprBooleanEq eq && eq.Right is ExprInt32Literal)
                        {
                            return and.Left;
                        }
                        if (subE is ExprColumnName c && c.Name == "UserId")
                        {
                            return new ExprColumnName("UserNewId");
                        }
                        return subE;
                    });

            //After
            Assert.AreEqual("SELECT [A0].[UserNewId],[A0].[FirstName],[A1].[CustomerId] " +
                            "FROM [dbo].[user] [A0] JOIN [dbo].[Customer] [A1] " +
                            "ON [A1].[UserNewId]=[A0].[UserNewId]", e.ToSql());
        }

        [Test]
        public void TestExportImportJson()
        {
            var tUser = Tables.User();
            var tCustomer = Tables.Customer();

            var selectExpr = Select(tUser.UserId, tUser.FirstName, tCustomer.CustomerId, Cast(Literal(12.8m), SqlType.Decimal((10,2))).As("Salary"))
                .From(tUser)
                .InnerJoin(tCustomer, on: tCustomer.UserId == tUser.UserId)
                .Where(tUser.Version == 5)
                .OrderBy(tUser.FirstName)
                .OffsetFetch(100, 5)
                .Done();

            using MemoryStream writer = new MemoryStream();
            selectExpr
                .SyntaxTree()
                .WalkThrough(new JsonWriter(), new Utf8JsonWriter(writer));

            var jsonText = Encoding.UTF8.GetString(writer.ToArray());

            var doc = JsonDocument.Parse(jsonText);

            var deserialized = ExprDeserializer.Deserialize(doc.RootElement, new JsonReader());

            Assert.AreEqual(selectExpr.ToSql(), deserialized.ToSql());
        }

        [Test]
        public void TestExportImportPlain()
        {
            var tUser = Tables.User();
            var tCustomer = Tables.Customer();

            var selectExpr = Select(tUser.UserId, tUser.FirstName, tCustomer.CustomerId, Cast(Literal(12.8m), SqlType.Decimal((10, 2))).As("Salary"))
                .From(tUser)
                .InnerJoin(tCustomer, on: tCustomer.UserId == tUser.UserId)
                .Where(tUser.Version == 5 & tUser.RegDate > new DateTime(2020, 10, 18, 1,2,3,400) & tUser.RegDate <= new DateTime(2021,01,01))
                .OrderBy(tUser.FirstName)
                .OffsetFetch(100, 5)
                .Done();


            var items = selectExpr.SyntaxTree().ExportToPlainList(PlainItem.Create);

            var res = ExprDeserializer.DeserializeFormPlainList(items);

            Assert.AreEqual(selectExpr.ToSql(), res.ToSql());
        }
        [Test]
        public void TestExportImportXml()
        {
            var tUser = Tables.User();
            var tCustomer = Tables.Customer();

            var selectExpr = Select(tUser.UserId, tUser.FirstName, tCustomer.CustomerId, Cast(Literal(12.8m), SqlType.Decimal((10, 2))).As("Salary"))
                .From(tUser)
                .InnerJoin(tCustomer, on: tCustomer.UserId == tUser.UserId)
                .Where(tUser.Version == 5 & tUser.RegDate > new DateTime(2020, 10, 18, 1,2,3,400) & tUser.RegDate <= new DateTime(2021,01,01))
                .OrderBy(tUser.FirstName)
                .OffsetFetch(100, 5)
                .Done();

            var sb = new StringBuilder();
            
            using XmlWriter writer = XmlWriter.Create(sb);
            selectExpr.SyntaxTree().ExportToXml(writer);

            var doc = new XmlDocument();
            doc.LoadXml(sb.ToString());

            var res = ExprDeserializer.DeserializeFormXml(doc.DocumentElement!);
            Assert.AreEqual(selectExpr.ToSql(), res.ToSql());
        }
    }
}