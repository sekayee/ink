﻿using System.Text;
using System.Collections.Generic;

namespace Ink.Runtime
{
    internal class InkcWriter
    {
        public InkcWriter(Story story)
        { 
            _story = story;
            _sb = new StringBuilder ();
        }
        
        public override string ToString()
        {
            if (_finalString == null) {
                int version = Story.inkVersionCurrent;
                var rootContainer = _story.mainContentContainer;

                Write ("inkc ");
                Write (version);
                Write ("\n");
                Write (rootContainer);

                _finalString = _sb.ToString ();
            }

            return _finalString;
        }

        void Write(string str)
        {
            _sb.Append (str);
        }

        void Write(int value)
        {
            _sb.Append (value);
            _sb.Append (" ");
        }

        void Write(float value)
        {
            _sb.Append (value);
            _sb.Append (" ");
        }

        void Write(Divert divert)
        {
            Write (">");

            if (divert.hasVariableTarget) {
                Write ("?");
                Write (divert.variableDivertName);
            } else {
                Write (divert.targetPathString);
            }
            Write (" ");

            if (divert.isExternal) {
                Write ("x");
                Write (divert.externalArgs);
            }

            if (divert.pushesToStack) {
                if (divert.stackPushType == PushPopType.Function)
                    Write ("f");
                else if (divert.stackPushType == PushPopType.Tunnel)
                    Write ("t");
            }

            Write (">");
        }

        void Write(Container container)
        {
            Write ("{");

            if (container.hasValidName) {
                Write ("'" + container.name + "'");
            }

            Write (container.content);

            var namedOnlyContent = container.namedOnlyContent;
            if (namedOnlyContent != null && namedOnlyContent.Count > 0) {
                Write ("[");

                foreach (var namedRuntimeObj in container.namedOnlyContent)
                    Write (namedRuntimeObj.Value);

                Write ("]");
            }

            if (container.countFlags != 0) {
                Write ("f");
                Write (container.countFlags);
            }
                
            Write("}");
        }

        void Write(List<Runtime.Object> runtimeObjList)
        {
            foreach (var obj in runtimeObjList) {
                Write (obj);
            }
        }
            
        void Write(Runtime.Object runtimeObj)
        {
            if (runtimeObj is Container) {
                Write ((Container)runtimeObj);
            } else if (runtimeObj is StringValue) {

                var strVal = (StringValue)runtimeObj;

                if (strVal.isNewline)
                    Write ("\n");
                else {
                    Write ("\"");
                    // TODO: Escape quotes
                    Write (strVal.value);
                    Write ("\"");
                }

            } else if (runtimeObj is Glue) {
                Write ("G");
                var glue = (Glue)runtimeObj;
                if (glue.isBi)
                    Write ("b");
                else if (glue.isRight)
                    Write (">");
                else
                    Write ("<");
            } else if (runtimeObj is ControlCommand) {
                Write ("#");
                Write (InkcControlCommand.GetName ((ControlCommand)runtimeObj));
            } else if (runtimeObj is NativeFunctionCall) {
                var call = (NativeFunctionCall)runtimeObj;
                Write ("." + call.name + " ");
            } else if (runtimeObj is IntValue) {
                Write (((IntValue)runtimeObj).value);
            } else if (runtimeObj is FloatValue) {
                Write (((FloatValue)runtimeObj).value);
            } else if (runtimeObj is Divert) {
                Write ((Divert)runtimeObj);
            }

            else {
                Write ("?");
            }
        }

        Story _story;
        StringBuilder _sb;
        string _finalString;
    }

    internal class InkcReader
    {
        public InkcReader(string str)
        {
            _str = str;
            _index = 0;
        }

        public Container ReadStoryWithContainer()
        {
            Require( ReadString ("inkc "), "Not valid inkc - no 'inkc' header tag");

            // TODO: Support more flexible versioning
            var version = (int)ReadNumberValue();
            Require(version == Story.inkVersionCurrent, "Incorrect ink version");

            ReadString ("\n");

            return ReadContainer ();
        }

        void Require(bool b, string errorMessage=null)
        {
            if (b == false) Error (errorMessage);
        }

        void Require(object val, string errorMessage=null)
        {
            if (val == null) Error (errorMessage);
        }

        void Error(string err = null)
        {
            if (err == null)
                err = "Error in inkc format";
            throw new System.Exception (err);
        }

        // Full syntax:
        // {'containerName'runtimeobjects[namedonlycontent]}
        Container ReadContainer()
        {
            Require (ReadString ("{"));

            var c = new Container ();

            if (ReadString ("'")) {
                c.name = ReadUntil ('\'');
            }

            while( !ReadString("}") ) {
                var obj = ReadRuntimeObject ();
                c.AddContent (obj);

                // Optional named content
                if (ReadString ("[")) {
                    while( !ReadString("]") ) {
                        var namedObj = ReadRuntimeObject ();
                        c.AddToNamedContentOnly ((INamedContent)namedObj);
                    }
                }

                if (ReadString ("f"))
                    c.countFlags = (int) ReadNumberValue ();
            }
                
            return c;
        }

        Divert ReadDivert()
        {
            Require(ReadString (">"));

            var divert = new Divert ();

            if (ReadString ("?"))
                divert.variableDivertName = ReadUntil (' ');
            else
                divert.targetPathString = ReadUntil (' ');

            if (ReadString ("x")) {
                divert.isExternal = true;
                divert.externalArgs = (int)ReadNumberValue ();
            }

            if (ReadString ("f")) {
                divert.pushesToStack = true;
                divert.stackPushType = PushPopType.Function;
            } else if (ReadString ("t")) {
                divert.pushesToStack = true;
                divert.stackPushType = PushPopType.Tunnel;
            }

            Require (ReadString (">"));

            return divert;
        }

        Runtime.Object ReadRuntimeObject()
        {
            char peekedChar = _str [_index];

            if (peekedChar >= '0' && peekedChar <= '9') {
                return Value.Create(ReadNumberValue ());
            }

            switch (peekedChar) {
            case '\n':
                ReadString ("\n");
                return new StringValue ("\n");

            case '"':
                ReadString ("\"");

                // TODO: Cope with escaped strings
                var str = new StringValue (ReadUntil ('"'));

                return str;

            case '#':
                ReadString ("#");
                return InkcControlCommand.WithName (ReadString (InkcControlCommand.NameLength));

            case '{':
                return ReadContainer ();

            case 'G':
                ReadString ("G");
                var glueTypeChar = ReadString (1);
                if (glueTypeChar == "b") return new Glue (GlueType.Bidirectional);
                else if( glueTypeChar == "<") return new Glue (GlueType.Left);
                else return new Glue (GlueType.Right);


            // Operation
            case '.':
                ReadString (".");
                var opName = ReadUntil (' ');
                return NativeFunctionCall.CallWithName (opName);

            // Divert
            case '>':
                return ReadDivert ();

            case '?':
                throw new System.NotImplementedException ();
            }
                
            return null;
        }

        object ReadNumberValue()
        {
            var numStr = ReadUntil (' ');

            int intVal;
            if (int.TryParse (numStr, out intVal))
                return intVal;
            else
                return float.Parse (numStr);
        }


        bool ReadString(string str)
        {
            if (_str.Length < _index + str.Length)
                return false;

            if (_str.Substring (_index, str.Length) == str) {
                _index += str.Length;
                return true;
            }

            return false;
        }

        string ReadString(int length)
        {
            if (_index + length > _str.Length)
                return null;

            var str = _str.Substring (_index, length);

            _index += length;

            return str;
        }

        string ReadUntil(char c)
        {
            var charPos = _str.IndexOf (c, _index);
            if (charPos == -1)
                return null;

            string foundStr = _str.Substring (_index, charPos - _index);

            // Step over terminator
            _index = charPos+1;

            return foundStr;
        }

        string _str;
        int _index;
    }

    static internal class InkcControlCommand
    {
        public const int NameLength = 2;

        public static ControlCommand WithName(string name)
        {
            SetupNamesIfNecessary ();

            ControlCommand.CommandType type;
            if (!_controlCommandTypes.TryGetValue (name, out type))
                return null;

            return new ControlCommand (type);
        }

        public static string GetName(ControlCommand command)
        {
            SetupNamesIfNecessary ();

            return _controlCommandNames [(int)command.commandType];
        }

        static void SetupNamesIfNecessary()
        {
            if (_controlCommandNames != null)
                return;
            
            _controlCommandNames = new string[(int)ControlCommand.CommandType.TOTAL_VALUES];
            _controlCommandTypes = new Dictionary<string, ControlCommand.CommandType> ();

            _controlCommandNames [(int)ControlCommand.CommandType.EvalStart] = "ev";
            _controlCommandNames [(int)ControlCommand.CommandType.EvalOutput] = "ou";
            _controlCommandNames [(int)ControlCommand.CommandType.EvalEnd] = "/e";
            _controlCommandNames [(int)ControlCommand.CommandType.Duplicate] = "du";
            _controlCommandNames [(int)ControlCommand.CommandType.PopEvaluatedValue] = "po";
            _controlCommandNames [(int)ControlCommand.CommandType.PopFunction] = "rt";
            _controlCommandNames [(int)ControlCommand.CommandType.PopTunnel] = ">>";
            _controlCommandNames [(int)ControlCommand.CommandType.BeginString] = "st";
            _controlCommandNames [(int)ControlCommand.CommandType.EndString] = "/s";
            _controlCommandNames [(int)ControlCommand.CommandType.NoOp] = "no";
            _controlCommandNames [(int)ControlCommand.CommandType.ChoiceCount] = "cc";
            _controlCommandNames [(int)ControlCommand.CommandType.TurnsSince] = "tu";
            _controlCommandNames [(int)ControlCommand.CommandType.VisitIndex] = "vi";
            _controlCommandNames [(int)ControlCommand.CommandType.SequenceShuffleIndex] = "se";
            _controlCommandNames [(int)ControlCommand.CommandType.StartThread] = "th";
            _controlCommandNames [(int)ControlCommand.CommandType.Done] = "dn";
            _controlCommandNames [(int)ControlCommand.CommandType.End] = "en";

            for (int i = 0; i < (int)ControlCommand.CommandType.TOTAL_VALUES; ++i) {
                string name = _controlCommandNames [i];
                if (name == null)
                    throw new System.Exception ("Control command not accounted for in serialisation");
                else {
                    _controlCommandTypes [name] = (ControlCommand.CommandType)i;
                }  
            }
        }

        static string[] _controlCommandNames;
        static Dictionary<string, ControlCommand.CommandType> _controlCommandTypes;
    }
}
