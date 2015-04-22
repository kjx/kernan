using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grace.Execution;
using Grace.Parsing;

namespace Grace.Execution
{
    /// <summary>Translates a tree of ParseNodes into Nodes</summary>
    public class ExecutionTreeTranslator : ParseNodeVisitor<Node>
    {
        /// <summary>Translate a tree rooted at a parse node for an
        /// object into the corresponding Node tree</summary>
        /// <param name="obj">Root of the tree</param>
        public Node Translate(ObjectParseNode obj)
        {
            return obj.Visit(this);
        }

        /// <summary>Default visit, which reports an error</summary>
        /// <inheritdoc />
        public Node Visit(ParseNode pn)
        {
            throw new Exception("No ParseNodeVisitor override provided for " + pn);
        }

        /// <inheritdoc />
        public Node Visit(ObjectParseNode obj)
        {
            ObjectConstructorNode ret = new ObjectConstructorNode(obj.Token, obj);
            foreach (ParseNode p in obj.Body)
            {
                if (!(p is CommentParseNode))
                    ret.Add(p.Visit<Node>(this));
            }
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(NumberParseNode n)
        {
            return new NumberNode(n.Token, n);
        }

        /// <inheritdoc />
        public Node Visit(StringLiteralParseNode n)
        {
            return new StringLiteralNode(n.Token, n);
        }

        /// <inheritdoc />
        public Node Visit(InterpolatedStringParseNode n)
        {
            Node ret = null;
            foreach (ParseNode part in n.Parts)
            {
                if (ret == null)
                {
                    ret = part.Visit(this);
                }
                else
                {
                    var errn = new ExplicitReceiverRequestNode(n.Token, n,
                            ret);
                    List<Node> args = new List<Node>();
                    if (!(part is StringLiteralParseNode))
                    {
                        var rpnAS = new RequestPartNode("asString",
                                new List<Node>(), new List<Node>());
                        var errnAS = new ExplicitReceiverRequestNode(n.Token,
                                n, part.Visit(this));
                        errnAS.AddPart(rpnAS);
                        args.Add(errnAS);
                    }
                    else
                    {
                        args.Add(part.Visit(this));
                    }
                    RequestPartNode rpn = new RequestPartNode("++",
                            new List<Node>(), args);
                    errn.AddPart(rpn);
                    ret = errn;
                }
            }
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(MethodDeclarationParseNode d)
        {
            MethodNode ret = new MethodNode(d.Token, d);
            string name = "";
            for (int i = 0; i < d.NameParts.Count; i++)
            {
                if (name != "")
                    name += " ";
                string partName = (d.NameParts[i] as IdentifierParseNode).Name;
                name += partName;
                List<ParseNode> partParams = d.Parameters[i];
                List<Node> parameters = new List<Node>();
                foreach (ParseNode p in partParams)
                {
                    IdentifierParseNode id = p as IdentifierParseNode;
                    TypedParameterParseNode tppn = p as TypedParameterParseNode;
                    VarArgsParameterParseNode vappn = p as VarArgsParameterParseNode;
                    if (id != null)
                        parameters.Add(new ParameterNode(id.Token, id));
                    else if (tppn != null)
                    {
                        parameters.Add(new ParameterNode(tppn.Token, tppn.Name as IdentifierParseNode, tppn.Type.Visit(this)));
                    }
                    else if (vappn != null)
                    {
                        // Inside could be either an identifier or a
                        // TypedParameterParseNode - check for both.
                        var inIPN = vappn.Name as IdentifierParseNode;
                        var inTPPN = vappn.Name as TypedParameterParseNode;
                        if (inIPN != null)
                            parameters.Add(new ParameterNode(inIPN.Token,
                                        inIPN,
                                        true // Variadic
                                        ));
                        else if (inTPPN != null)
                            parameters.Add(new ParameterNode(inTPPN.Token,
                                        inTPPN.Name as IdentifierParseNode,
                                        true, // Variadic
                                        inTPPN.Type.Visit(this)
                                        ));
                    }
                    else
                    {
                        throw new Exception("unimplemented - unusual parameters");
                    }
                }
                List<Node> generics = new List<Node>();
                foreach (ParseNode p in d.Generics[i])
                {
                    IdentifierParseNode id = p as IdentifierParseNode;
                    if (id != null)
                    {
                        generics.Add(new IdentifierNode(id.Token, id));
                    }
                    else
                    {
                        throw new Exception("unimplemented - bad generic parameters");
                    }
                }
                RequestPartNode rpn = new RequestPartNode(partName, generics, parameters);
                ret.AddPart(rpn);
            }
            if (d.Annotations != null
                    && d.Annotations.HasAnnotation("confidential"))
                ret.Confidential = true;
            foreach (ParseNode p in d.Body)
                if (!(p is CommentParseNode))
                    ret.Add(p.Visit(this));
            if (d.Body.Count > 0 && d.Body[d.Body.Count - 1] is
                    ObjectParseNode)
            {
                // This method returns a fresh object
                ret.Fresh = true;
            }
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(IdentifierParseNode i)
        {
            ImplicitReceiverRequestNode ret = new ImplicitReceiverRequestNode(i.Token, i);
            RequestPartNode rpn = new RequestPartNode(i.Name, new List<Node>(), new List<Node>());
            ret.AddPart(rpn);
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(ImplicitReceiverRequestParseNode irrpn)
        {
            ImplicitReceiverRequestNode ret = new ImplicitReceiverRequestNode(irrpn.Token, irrpn);
            for (int i = 0; i < irrpn.Arguments.Count; i++)
            {
                RequestPartNode rpn = new RequestPartNode((irrpn.NameParts[i] as IdentifierParseNode).Name,
                    map(irrpn.GenericArguments[i]),
                    map(irrpn.Arguments[i]));
                ret.AddPart(rpn);
            }
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(ExplicitReceiverRequestParseNode irrpn)
        {
            ExplicitReceiverRequestNode ret = new ExplicitReceiverRequestNode(irrpn.Token, irrpn,
                irrpn.Receiver.Visit(this));
            for (int i = 0; i < irrpn.Arguments.Count; i++)
            {
                RequestPartNode rpn = new RequestPartNode((irrpn.NameParts[i] as IdentifierParseNode).Name,
                    map(irrpn.GenericArguments[i]),
                    map(irrpn.Arguments[i]));
                ret.AddPart(rpn);
            }
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(PrefixOperatorParseNode popn)
        {
            ExplicitReceiverRequestNode ret = new ExplicitReceiverRequestNode(popn.Token, popn,
                popn.Receiver.Visit(this));
            RequestPartNode rpn = new RequestPartNode("prefix" + popn.Name,
                    new List<Node>(),
                    new List<Node>());
            ret.AddPart(rpn);
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(OperatorParseNode opn)
        {
            ExplicitReceiverRequestNode ret = new ExplicitReceiverRequestNode(opn.Token, opn, opn.Left.Visit(this));
            List<Node> args = new List<Node>();
            args.Add(opn.Right.Visit(this));
            RequestPartNode rpn = new RequestPartNode(opn.name, new List<Node>(), args);
            ret.AddPart(rpn);
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(VarDeclarationParseNode vdpn)
        {
            Node val = null;
            Node type = null;
            if (vdpn.Value != null)
                val = vdpn.Value.Visit(this);
            if (vdpn.Type != null)
                type = vdpn.Type.Visit(this);
            var ret = new VarDeclarationNode(vdpn.Token, vdpn,
                    val, type);
            if (vdpn.Annotations != null
                    && vdpn.Annotations.HasAnnotation("public"))
            {
                ret.Readable = true;
                ret.Writable = true;
            }
            if (vdpn.Annotations != null
                    && vdpn.Annotations.HasAnnotation("readable"))
                ret.Readable = true;
            if (vdpn.Annotations != null
                    && vdpn.Annotations.HasAnnotation("writable"))
                ret.Writable = true;
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(DefDeclarationParseNode vdpn)
        {
            Node val = null;
            Node type = null;
            if (vdpn.Value != null)
                val = vdpn.Value.Visit(this);
            if (vdpn.Type != null)
                type = vdpn.Type.Visit(this);
            var ret = new DefDeclarationNode(vdpn.Token, vdpn,
                    val, type);
            if (vdpn.Annotations != null
                    && vdpn.Annotations.HasAnnotation("public"))
                ret.Public = true;
            if (vdpn.Annotations != null
                    && vdpn.Annotations.HasAnnotation("readable"))
                ret.Public = true;
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(BindParseNode bpn)
        {
            var ret = bpn.Left.Visit(this);
            var right = bpn.Right.Visit(this);
            var lrrn = ret as RequestNode;
            if (lrrn != null)
            {
                lrrn.MakeBind(right);
            }
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(BlockParseNode d)
        {
            var parameters = new List<Node>();
            foreach (ParseNode p in d.Parameters)
            {
                IdentifierParseNode id = p as IdentifierParseNode;
                TypedParameterParseNode tppn = p as TypedParameterParseNode;
                if (id != null)
                    parameters.Add(new ParameterNode(id.Token, id));
                else if (tppn != null)
                {
                    parameters.Add(new ParameterNode(tppn.Token, tppn.Name as IdentifierParseNode, tppn.Type.Visit(this)));
                }
                else if (p is NumberParseNode || p is StringLiteralParseNode
                        || p is OperatorParseNode)
                {
                    parameters.Add(p.Visit(this));
                }
                else
                {
                    throw new Exception("unimplemented - unusual parameters");
                }
            }
            var ret = new BlockNode(d.Token, d,
                    parameters,
                    map(d.Body));
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(ClassDeclarationParseNode d)
        {
            var clsObj = new ObjectParseNode(d.Token);
            var constructor = new MethodDeclarationParseNode(d.Token);
            constructor.NameParts = d.NameParts;
            constructor.Parameters = d.Parameters;
            constructor.Generics = d.Generics;
            constructor.ReturnType = d.ReturnType;
            constructor.Annotations = d.Annotations;
            var instanceObj = new ObjectParseNode(d.Token);
            instanceObj.Body = d.Body;
            constructor.Body.Add(instanceObj);
            clsObj.Body.Add(constructor);
            var dpn = new DefDeclarationParseNode(d.Token,
                    d.BaseName,
                    clsObj,
                    null, // Type
                    null);
            return dpn.Visit(this);
        }

        /// <inheritdoc />
        public Node Visit(ReturnParseNode rpn)
        {
            if (rpn.ReturnValue == null)
                return new ReturnNode(rpn.Token, rpn,
                        null);
            return new ReturnNode(rpn.Token, rpn,
                    rpn.ReturnValue.Visit(this));
        }

        /// <inheritdoc />
        public Node Visit(CommentParseNode cpn)
        {
            return new NoopNode(cpn.Token, cpn);
        }

        /// <inheritdoc />
        public Node Visit(TypeStatementParseNode tspn)
        {
            var meth = new MethodDeclarationParseNode(tspn.Token);
            PartParameters pp = meth.AddPart(tspn.BaseName);
            pp.Generics.AddRange(tspn.genericParameters);
            var tpn = tspn.Body as TypeParseNode;
            if (tpn != null)
            {
                tpn.Name = (tspn.BaseName as IdentifierParseNode).Name;
            }
            meth.Body.Add(tspn.Body);
            return meth.Visit(this);
        }

        /// <inheritdoc />
        public Node Visit(TypeParseNode tpn)
        {
            var ret = new TypeNode(tpn.Token, tpn);
            if (tpn.Name != null)
                ret.Name = tpn.Name;
            foreach (var p in tpn.Body)
                ret.Body.Add((MethodTypeNode)p.Visit(this));
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(TypeMethodParseNode d)
        {
            var ret = new MethodTypeNode(d.Token, d);
            if (d.ReturnType != null)
            {
                ret.Returns = d.ReturnType.Visit(this);
            }
            string name = "";
            for (int i = 0; i < d.NameParts.Count; i++)
            {
                if (name != "")
                    name += " ";
                string partName = (d.NameParts[i] as IdentifierParseNode).Name;
                name += partName;
                List<ParseNode> partParams = d.Parameters[i];
                List<Node> parameters = new List<Node>();
                foreach (ParseNode p in partParams)
                {
                    IdentifierParseNode id = p as IdentifierParseNode;
                    TypedParameterParseNode tppn = p as TypedParameterParseNode;
                    VarArgsParameterParseNode vappn = p as VarArgsParameterParseNode;
                    if (id != null)
                        parameters.Add(new IdentifierNode(id.Token, id));
                    else if (tppn != null)
                    {
                        parameters.Add(new ParameterNode(tppn.Token, tppn.Name as IdentifierParseNode, tppn.Type.Visit(this)));
                    }
                    else if (vappn != null)
                    {
                        // Inside could be either an identifier or a
                        // TypedParameterParseNode - check for both.
                        var inIPN = vappn.Name as IdentifierParseNode;
                        var inTPPN = vappn.Name as TypedParameterParseNode;
                        if (inIPN != null)
                            parameters.Add(new ParameterNode(inIPN.Token,
                                        inIPN,
                                        true // Variadic
                                        ));
                        else if (inTPPN != null)
                            parameters.Add(new ParameterNode(inTPPN.Token,
                                        inTPPN.Name as IdentifierParseNode,
                                        true, // Variadic
                                        inTPPN.Type.Visit(this)
                                        ));
                    }
                    else
                    {
                        throw new Exception("unimplemented - unusual parameters");
                    }
                }
                List<Node> generics = new List<Node>();
                foreach (ParseNode p in d.Generics[i])
                {
                    IdentifierParseNode id = p as IdentifierParseNode;
                    if (id != null)
                    {
                        generics.Add(new IdentifierNode(id.Token, id));
                    }
                    else
                    {
                        throw new Exception("unimplemented - bad generic parameters");
                    }
                }
                RequestPartNode rpn = new RequestPartNode(partName, generics, parameters);
                ret.AddPart(rpn);
            }
            return ret;
        }

        /// <inheritdoc />
        public Node Visit(ImportParseNode ipn)
        {
            Node type = null;
            if (ipn.Type != null)
                type = ipn.Type.Visit(this);
            return new ImportNode(ipn.Token, ipn, type);
        }

        /// <inheritdoc />
        public Node Visit(DialectParseNode dpn)
        {
            return new DialectNode(dpn.Token, dpn);
        }

        /// <summary>Transforms a list of ParseNodes into a list of the
        /// corresponding Nodes</summary>
        private List<Node> map(List<ParseNode> l)
        {
            List<Node> ret = new List<Node>();
            foreach (ParseNode p in l)
                if (!(p is CommentParseNode))
                    ret.Add(p.Visit(this));
            return ret;
        }
    }

}
