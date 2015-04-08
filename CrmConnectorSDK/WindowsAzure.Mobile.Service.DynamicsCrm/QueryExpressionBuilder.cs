﻿using Microsoft.Data.OData.Query;
using Microsoft.Data.OData.Query.SemanticAst;
using Microsoft.WindowsAzure.Mobile.Service.Tables;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;
using System.Web.Http.OData.Query;

namespace Microsoft.WindowsAzure.Mobile.Service.DynamicsCrm
{
    /// <summary>
    /// Used to build a QueryExpression from ODataQueryOptions.
    /// </summary>
    /// <typeparam name="TTableData">The data transfer object type returned by the mobile service.</typeparam>
    /// <typeparam name="TEntity">The strongly typed entity, generated by crmsvcutil.</typeparam>
    internal class QueryExpressionBuilder<TTableData, TEntity>
        where TTableData : class, ITableData
        where TEntity : Entity
    {
        protected string EntityLogicalName { get; set; }
        protected ODataQueryOptions ODataQueryOptions { get; set; }
        protected IEntityMapper<TTableData, TEntity> EntityMapper { get; set; }

        /// <summary>
        /// QueryExpressionBuilder constructor.
        /// </summary>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="query">The ODataQueryOptions that will be translated into a QueryExpression.</param>
        /// <param name="entityMapper">The mapper used to convert between TTableData and TEntity types.</param>
        public QueryExpressionBuilder(string entityLogicalName, ODataQueryOptions query, 
            IEntityMapper<TTableData, TEntity> entityMapper)
        {
            EntityLogicalName = entityLogicalName;
            ODataQueryOptions = query;
            EntityMapper = entityMapper;
        }

        /// <summary>
        /// Used to get the resulting QueryExpression.
        /// </summary>
        /// <returns>The QueryExpression equivalent of the ODataQueryOptions passed to the constructor.</returns>
        public QueryExpression GetQueryExpression()
        {
            QueryExpression crmQuery = new QueryExpression(this.EntityLogicalName);

            ODataValidationSettings settings = new ODataValidationSettings();
            
            settings.AllowedLogicalOperators =
                AllowedLogicalOperators.Equal |
                AllowedLogicalOperators.NotEqual |
                AllowedLogicalOperators.GreaterThan |
                AllowedLogicalOperators.GreaterThanOrEqual |
                AllowedLogicalOperators.LessThan |
                AllowedLogicalOperators.LessThanOrEqual |
                AllowedLogicalOperators.And |
                AllowedLogicalOperators.Or;
            
            settings.AllowedFunctions = 
                AllowedFunctions.StartsWith | 
                AllowedFunctions.SubstringOf | 
                AllowedFunctions.EndsWith;

            settings.AllowedArithmeticOperators = AllowedArithmeticOperators.None;

            settings.MaxTop = 5000;
            settings.AllowedQueryOptions = AllowedQueryOptions.All ^ AllowedQueryOptions.Expand;

            this.ODataQueryOptions.Validate(settings);

            UpdateCriteriaFromFilter(crmQuery.Criteria, ODataQueryOptions.Filter);
            UpdateColumnSetFromSelectExpand(crmQuery.ColumnSet, ODataQueryOptions.SelectExpand);
            UpdatePagingFromSkipAndTop(crmQuery.PageInfo, ODataQueryOptions.Skip, ODataQueryOptions.Top);
            UpdateOrdersFromOrderBy(crmQuery.Orders, ODataQueryOptions.OrderBy);

            return crmQuery;
        }

        /// <summary>
        /// Append OData filtering to the FilterExpression.
        /// </summary>
        /// <param name="criteria">Filter container for the QueryExpression.</param>
        /// <param name="filter">OData filtering details.</param>
        private void UpdateCriteriaFromFilter(FilterExpression criteria, FilterQueryOption filter)
        {
            if (filter != null)
            {
                if (filter.Context != null)
                {
                    if (filter.Context.ElementType.TypeKind != Microsoft.Data.Edm.EdmTypeKind.Entity)
                    {
                        throw new NotImplementedException(String.Format("Unsupported OData element type kind: {0}", filter.Context.ElementType.TypeKind));
                    }
                }

                UpdateCriteriaFromExpression(criteria, filter.FilterClause.Expression);
            }
        }

        /// <summary>
        /// Append OData filtering to the FilterExpression.
        /// </summary>
        /// <param name="criteria">Filter container for the QueryExpression.</param>
        /// <param name="expression">An expression containing OData filtering details.</param>
        private void UpdateCriteriaFromExpression(FilterExpression criteria, SingleValueNode expression)
        {
            switch (expression.Kind)
            {
                case QueryNodeKind.BinaryOperator:
                    UpdateCriteriaFromBinaryExpression(criteria, (BinaryOperatorNode)expression);
                    break;

                case QueryNodeKind.Convert:
                    UpdateCriteriaFromExpression(criteria, ((ConvertNode)expression).Source);
                    break;

                case QueryNodeKind.SingleValueFunctionCall:
                    UpdateCriteriaFromSingleValueFunctionCall(criteria, (SingleValueFunctionCallNode)expression);
                    break;

                default:
                    throw new NotImplementedException(String.Format("Unsupported expression kind: \'{0}\'.", expression.Kind));
            }
        }

        /// <summary>
        /// Append OData filtering to the FilterExpression.
        /// </summary>
        /// <param name="criteria">Filter container for the QueryExpression.</param>
        /// <param name="expression">A binary expression containing OData filtering details.</param>
        private void UpdateCriteriaFromBinaryExpression(FilterExpression criteria, BinaryOperatorNode expression)
        {
            ConditionOperator crmOperator;
            switch (expression.OperatorKind)
            {
                case BinaryOperatorKind.And:
                case BinaryOperatorKind.Or:
                    var childCriteria = new FilterExpression(expression.OperatorKind == BinaryOperatorKind.And ? LogicalOperator.And : LogicalOperator.Or);
                    criteria.AddFilter(childCriteria);
                    UpdateCriteriaFromExpression(childCriteria, expression.Left);
                    UpdateCriteriaFromExpression(childCriteria, expression.Right);
                    return;

                case BinaryOperatorKind.Equal:
                    if (expression.Left.Kind == QueryNodeKind.SingleValueFunctionCall &&
                        expression.Right.Kind == QueryNodeKind.Constant &&
                        (bool)((ConstantNode)expression.Right).Value == true)
                    {
                        UpdateCriteriaFromSingleValueFunctionCall(criteria, (SingleValueFunctionCallNode)expression.Left);
                        return;
                    }
                    else
                    {
                        crmOperator = ConditionOperator.Equal;
                    }
                    break;

                case BinaryOperatorKind.NotEqual:
                    crmOperator = ConditionOperator.NotEqual;
                    break;

                case BinaryOperatorKind.GreaterThan:
                    crmOperator = ConditionOperator.GreaterThan;
                    break;

                case BinaryOperatorKind.GreaterThanOrEqual:
                    crmOperator = ConditionOperator.GreaterEqual;
                    break;

                case BinaryOperatorKind.LessThan:
                    crmOperator = ConditionOperator.LessThan;
                    break;

                case BinaryOperatorKind.LessThanOrEqual:
                    crmOperator = ConditionOperator.LessEqual;
                    break;

                default:
                    throw new NotImplementedException(String.Format("Unsupported operator \'{0}\'.", expression.OperatorKind));
            }

            var value = GetValue(expression.Right);
            var attributeName = GetAttributeName(expression.Left);

            if (value == null)
            {
                if (crmOperator == ConditionOperator.Equal) crmOperator = ConditionOperator.Null;
                if (crmOperator == ConditionOperator.NotEqual) crmOperator = ConditionOperator.NotNull;
                
                criteria.AddCondition(attributeName, crmOperator);
            }
            else
            {
                criteria.AddCondition(attributeName, crmOperator, value);
            }
        }

        /// <summary>
        /// Append OData filtering to the FilterExpression.
        /// </summary>
        /// <param name="criteria">Filter container for the QueryExpression.</param>
        /// <param name="expression">A single value expression containing OData filtering details.</param>
        private void UpdateCriteriaFromSingleValueFunctionCall(FilterExpression criteria, SingleValueFunctionCallNode expression)
        {
            QueryNode[] arguments = expression.Arguments.ToArray();

            switch (expression.Name.ToLowerInvariant())
            {
                case "startswith":
                    if (arguments.Length != 2) throw new InvalidOperationException("\'startswith\' expects 2 arguments.");
                    criteria.AddCondition(GetAttributeName(arguments[0]), ConditionOperator.BeginsWith, GetValue(arguments[1]));
                    break;

                case "endswith":
                    if (arguments.Length != 2) throw new InvalidOperationException("\'endswith\' expects 2 arguments.");
                    criteria.AddCondition(GetAttributeName(arguments[0]), ConditionOperator.EndsWith, GetValue(arguments[1]));
                    break;

                case "substringof":
                    if (arguments.Length != 2) throw new InvalidOperationException("\'substringof\' expects 2 arguments.");
                    criteria.AddCondition(GetAttributeName(arguments[0]), ConditionOperator.Contains, GetValue(arguments[1]));
                    break;

                default:
                    throw new NotImplementedException(String.Format("Unsupported function \'{0}\'.", expression.Name.ToLowerInvariant()));
            }
        }

        /// <summary>
        /// Calculates a literal value based on an OData QueryNode.
        /// </summary>
        /// <param name="queryNode">The query node.</param>
        /// <returns>A literal value to use in the QueryExpression criteria.</returns>
        private static object GetValue(QueryNode queryNode)
        {
            switch (queryNode.Kind)
            {
                case QueryNodeKind.Constant:
                    object value = ((ConstantNode)queryNode).Value;

                    if (value is DateTimeOffset)
                    {
                        value = ((DateTimeOffset)value).UtcDateTime;
                    }
                    
                    return value;

                case QueryNodeKind.Convert:
                    return GetValue(((ConvertNode)queryNode).Source);

                default:
                    throw new NotImplementedException(String.Format("Unsupported value type \'{0}\'.", queryNode.Kind));
            }
        }

        /// <summary>
        /// Converts an OData path to a CRM attribute name.
        /// </summary>
        /// <param name="queryNode">The query node that describes the OData path.</param>
        /// <returns>CRM attribute name.</returns>
        private string GetAttributeName(QueryNode queryNode)
        {
            switch (queryNode.Kind)
            {
                case QueryNodeKind.Convert:
                    return GetAttributeName(((ConvertNode)queryNode).Source);

                case QueryNodeKind.SingleValuePropertyAccess:
                    return EntityMapper.GetAttributeName(((SingleValuePropertyAccessNode)queryNode).Property.Name);

                default:
                    throw new NotImplementedException(String.Format("Unsupported property selector type \'{0}\'.", queryNode.Kind));
            }
        }

        /// <summary>
        /// Updates the QueryExpression's ColumnSet to include the specified attributes only.
        /// </summary>
        /// <param name="columnSet">The target CRM ColumnSet.</param>
        /// <param name="selectExpand">The OData select/expand options.</param>
        private void UpdateColumnSetFromSelectExpand(ColumnSet columnSet, SelectExpandQueryOption selectExpand)
        {
            if (selectExpand == null || selectExpand.SelectExpandClause == null || selectExpand.SelectExpandClause.AllSelected)
            {
                foreach (var attributeName in this.EntityMapper.GetAttributeNames())
                {
                    columnSet.AddColumn(attributeName);
                }
            }
            else
            {
                foreach (var item in selectExpand.SelectExpandClause.SelectedItems.OfType<PathSelectItem>())
                {
                    var pathItem = item.SelectedPath.OfType<PropertySegment>().Single();
                    if(pathItem.Property.Name != "Deleted")
                    {
                        var attributeName = EntityMapper.GetAttributeName(pathItem.Property.Name);
                        columnSet.AddColumn(attributeName);
                    }
                }
            }

            columnSet.Columns.Remove("id");
        }

        /// <summary>
        /// Updates the QueryExpression's paging settings based on OData parameters.
        /// </summary>
        /// <param name="pagingInfo">The target PagingInfo</param>
        /// <param name="skip">The OData options that describe how many records to skip.</param>
        /// <param name="top">The OData options that describe how many records per page.</param>
        private static void UpdatePagingFromSkipAndTop(PagingInfo pagingInfo, SkipQueryOption skip, TopQueryOption top)
        {
            if (top == null)
            {
                pagingInfo.Count = 50;
            }
            else
            {
                pagingInfo.Count = top.Value;
            }

            if (skip == null)
            {
                pagingInfo.PageNumber = 1;
            }
            else
            {
                // When syncing for offline, the client will always request a page immediately after
                // the last record.  So if there are only 7 records, it will request with $top=50 and
                // $skip=7.  To handle these cases when $skip is not evenly divisible by $top, we round
                // up, which will send back an empty result set on the last page and complete the sync.
                pagingInfo.PageNumber = (int)Math.Ceiling((double)skip.Value / pagingInfo.Count) + 1;
            }
        }

        /// <summary>
        /// Updates the QueryExpression's sorting options.
        /// </summary>
        /// <param name="orders">The QueryExpression's collection of sorting options.</param>
        /// <param name="orderBy">The OData options for sorting.</param>
        private void UpdateOrdersFromOrderBy(DataCollection<OrderExpression> orders, OrderByQueryOption orderBy)
        {
            if (orderBy != null)
            {
                foreach (var node in orderBy.OrderByNodes.OfType<OrderByPropertyNode>())
                {
                    var attributeName = EntityMapper.GetAttributeName(node.Property.Name);
                    var direction = node.Direction == OrderByDirection.Ascending ? OrderType.Ascending : OrderType.Descending;
                    orders.Add(new OrderExpression(attributeName, direction));
                }
            }
        }
    }
}
