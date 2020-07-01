﻿open System
open System.IO
open System.Text
open FSharp.Data

let indentSpaces = 4

type GetTypesState = {
    ParentPath:string list
    Parent:JsonValue
    CurSb:string
    CurCodeSb:string
    CurJsonSb:string
    ChildRecords:string list
    CurIndent:int
    Types:string list
}


let loadJson (file:string) =
    // Grab the JSON from a live chart object using something like
    // JSON.stringify(Highcharts.charts[0].options)
    // in the browser console, then save to a file to be loaded here.

    File.ReadAllText(file)


let jsonToSchema (namespaceName:string) (moduleName:string) (json:string) =
    let pascalise (s:string) =
        let sTrim =
            s.Trim()
            |> Seq.toList

        match sTrim with
        | hd::tl ->
            Char.ToUpper(hd) :: tl
            |> List.toArray
            |> String
        | _ ->
            sTrim
            |> List.toArray
            |> String
        
    let (>~>) s str =
        s + Environment.NewLine + str

    let (~~) i =
        if i <= 0 then
            ""
        else
            [|for _ in 1..i -> String(' ',indentSpaces)|]
            |> Seq.reduce (+)

    let addPath p t =
        t::p

    let remPath p =
        match p with
        | _::tl ->
            tl
        | _ ->
            p

    let strRep (strFrom:string) (strTo:string) (s:string) =
       s.Replace(strFrom, strTo)

    let stripStart stripStr (s:string) =
        if String.IsNullOrEmpty stripStr then
            s
        else
            if s.StartsWith stripStr then s.Substring(stripStr.Length)
            else s

    let pathToString p =
        if List.isEmpty p then
            ""
        else
            p
            |> List.reduce (fun s p -> sprintf "%s.%s" p s)
    
    let pathToType p =
        if List.isEmpty p then
            ""
        else
            p
            |> List.reduce (fun s p -> sprintf "%s_%s" (pascalise p) (pascalise s))

    let pathToJson p =
        p
        |> List.fold (fun s p -> sprintf "\\\\\\\"%s\\\\\\\":{%s}" p s) "##VALUE##"
        |> strRep "{##VALUE##}" ("%" + "s")
        |> stripStart "\\\\\\\"Figure\\\\\\\":"
     
    let deKeyword s =
        match s with
        | "type" -> "_type"
        | "global" -> "_global"
        | _ -> s
    
    let rec getTypes (state:GetTypesState) (current:string*JsonValue) =
        let curKey,curVal = current
        let curKeySafe = deKeyword curKey
        let curPath = addPath state.ParentPath curKey
        
        match curVal with
        | JsonValue.Record(r) ->
            let nextState =
                match r with
                | [||] ->
                    { ParentPath = curPath; Parent = curVal; CurSb = ""; CurCodeSb = "dummy:string"; CurJsonSb = ""; ChildRecords = ["dummy"]; CurIndent = state.CurIndent; Types = state.Types }
                | _ ->
                    r
                    |> Array.fold getTypes { ParentPath = curPath; Parent = curVal; CurSb = ""; CurCodeSb = ""; CurJsonSb = ""; ChildRecords = []; CurIndent = state.CurIndent; Types = state.Types }
            
            let curType =
                let recordsStr =
                    if Seq.isEmpty r then "o.dummy" else String.Join(' ',List.rev nextState.ChildRecords)
                let recFormats =
                    if List.isEmpty nextState.ChildRecords then ""
                    else
                        nextState.ChildRecords
                        |> List.rev
                        |> List.fold (fun str child -> sprintf "%s,\\\\\\\"%s\\\\\\\":%ss" str child "%") ""
                        |> fun s -> s.Substring(1)
                ""
                >~> sprintf "%s///<summary>%s : <code>{| %s |}</code></summary>" ~~1 (pathToType curPath) nextState.CurCodeSb
                >~> sprintf "%stype %s() = %s" ~~1 (pathToType curPath) nextState.CurSb
                >~> sprintf "%sstatic member ToJson (o:{| %s |}) =" ~~2 nextState.CurCodeSb
                >~> sprintf "%s" nextState.CurJsonSb
                >~> sprintf "%ssprintf \"{%s}\" %s" ~~3 recFormats recordsStr
                >~> ""
                >~> sprintf "%smember this.Set (o:{| %s |}) =" ~~2 nextState.CurCodeSb
                >~> sprintf "%supdate 0 (sprintf \"%s\" (%s.ToJson o))" ~~3 (pathToJson curPath) (pathToType curPath)
                >~> ""
            
            let curSb =
                state.CurSb
                >~> ""
                >~> sprintf "%s///<summary>%s : <code>{| %s |}</code></summary>" ~~2 (pathToType curPath) nextState.CurCodeSb
                >~> sprintf "%smember this.%s = %s()" ~~2 curKeySafe (pathToType curPath)

            let curCodeSb =
                match state.Parent with
                | JsonValue.Record(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s: {| %s |}" curKeySafe nextState.CurCodeSb
                    else
                        sprintf "; %s: {| %s |}" curKeySafe nextState.CurCodeSb
                | JsonValue.Array(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "{| %s |}" nextState.CurCodeSb
                    else
                        sprintf "; {| %s |}" nextState.CurCodeSb
                | _ ->
                    sprintf "; %s: {| %s |}" curKeySafe nextState.CurCodeSb

            let curJsonSb =
                if String.IsNullOrEmpty state.CurJsonSb then
                    sprintf "%slet %s = %s.ToJson o.%s" ~~state.CurIndent curKeySafe (pathToType curPath) curKeySafe
                else
                    sprintf "\n%slet %s = %s.ToJson o.%s" ~~state.CurIndent curKeySafe (pathToType curPath) curKeySafe
            
            { state with
                CurSb = curSb
                CurCodeSb = state.CurCodeSb + curCodeSb
                CurJsonSb = state.CurJsonSb + curJsonSb
                ChildRecords = curKeySafe :: state.ChildRecords
                CurIndent = state.CurIndent
                Types = curType::nextState.Types
            }
        | JsonValue.Array(arr) ->
            let nextState =
                match Array.tryHead arr with
                | None ->
                    // Assume type is float array
                    { state with CurSb = "type + child sb"; CurCodeSb = state.CurCodeSb + "; curKey:string seq"; Types = "curType seqqy"::state.Types }
                | Some(hd) ->
                    getTypes { state with ParentPath = curPath; Parent = curVal; CurSb = ""; CurCodeSb = ""; CurJsonSb = ""; ChildRecords = []; CurIndent = state.CurIndent; Types = state.Types } ("item",hd)
            
            let curType =
                ""
                >~> sprintf "%s///<summary>%s : <code>%s seq</code></summary>" ~~1 (pathToType curPath) nextState.CurCodeSb
                >~> sprintf "%stype %s() =" ~~1 (pathToType curPath)
                >~> sprintf "%slet item = %s()" ~~2 (pathToType (addPath curPath "item"))
                >~> ""
                >~> sprintf "%sstatic member ToJson (o:%s seq) =" ~~2 nextState.CurCodeSb // TODO: get actual item index here, from last call to get() accessor
                >~> sprintf "%sif Seq.isEmpty o then \"[]\"" ~~3
                >~> sprintf "%selse" ~~3
                >~> sprintf "%so" ~~4
                >~> sprintf "%s|> Seq.fold (fun s oi -> sprintf \"%ss,[%ss]\" s ( %s.ToJson oi)) \"\"" ~~4 "%" "%" (pathToType (addPath curPath "item"))
                >~> sprintf "%s|> fun s -> s.Substring(1)" ~~4 
                
                >~> ""
                >~> sprintf "%smember this.Set (o:%s seq) =" ~~2 nextState.CurCodeSb
                >~> sprintf "%supdate 0 (sprintf \"%s\" (%s.ToJson o))" ~~3 (pathToJson curPath) (pathToType curPath)
                >~> ""
                >~> sprintf "%smember this.Item" ~~2
                >~> sprintf "%swith get(_) =" ~~3
                >~> sprintf "%sitem" ~~4
            
            let curSb = 
                state.CurSb
                >~> ""
                >~> sprintf "%s///<summary>%s : <code>%s seq</code></summary>" ~~2 (pathToType curPath) nextState.CurCodeSb
                >~> sprintf "%smember this.%s = %s()" ~~2 curKeySafe (pathToType curPath)

            let curCodeSb =
                match state.Parent with
                | JsonValue.Record(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s:%s seq" curKeySafe nextState.CurCodeSb
                    else
                        sprintf "; %s:%s seq" curKeySafe nextState.CurCodeSb
                | JsonValue.Array(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s seq" nextState.CurCodeSb
                    else
                        sprintf "; %s seq" nextState.CurCodeSb
                | _ ->
                    sprintf "; %s:%s seq" curKeySafe nextState.CurCodeSb

            let curJsonSb =
                if String.IsNullOrEmpty state.CurJsonSb then
                    sprintf "%slet %s = %s.ToJson o.%s" ~~state.CurIndent curKeySafe (pathToType curPath) curKeySafe
                else
                    sprintf "\n%slet %s = %s.ToJson o.%s" ~~state.CurIndent curKeySafe (pathToType curPath) curKeySafe
            
            { state with
                CurSb = curSb
                CurCodeSb = state.CurCodeSb + curCodeSb
                CurJsonSb = state.CurJsonSb + curJsonSb
                ChildRecords = curKeySafe :: state.ChildRecords
                CurIndent = state.CurIndent
                Types = curType::nextState.Types
            }
        | JsonValue.String(_) ->
            let curSb = 
                state.CurSb
                >~> ""
                >~> sprintf "%s///<summary>%s : <code>string</code></summary>" ~~2 (pathToType curPath)
                >~> sprintf "%smember this.%s = %s()" ~~2 curKeySafe (pathToType curPath)

            let curCodeSb =
                match state.Parent with
                | JsonValue.Record(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s:string" curKeySafe
                    else
                        sprintf "; %s:string" curKeySafe
                | _ ->
                    sprintf "string"

            let curJsonSb =
                if String.IsNullOrEmpty state.CurJsonSb then
                    sprintf "%slet %s = sprintf \"\\\\\\\"%ss\\\\\\\"\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe
                else
                    sprintf "\n%slet %s = sprintf \"\\\\\\\"%ss\\\\\\\"\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe
            

            let curType =
                ""
                >~> sprintf "%s///<summary>%s : <code>string</code></summary>" ~~1 (pathToType curPath)
                >~> sprintf "%stype %s() =" ~~1 (pathToType curPath)
                >~> sprintf "%sstatic member ToJson (o:string) =" ~~2
                >~> sprintf "%ssprintf \"\\\\\\\"%ss\\\\\\\"\" o" ~~3 "%"
                >~> ""
                >~> sprintf "%smember this.Set (o:string) =" ~~2
                >~> sprintf "%supdate 0 (sprintf \"%s\" (%s.ToJson o))" ~~3 (pathToJson curPath) (pathToType curPath)

            { state with
                CurSb = curSb
                CurCodeSb = state.CurCodeSb + curCodeSb
                CurJsonSb = state.CurJsonSb + curJsonSb
                ChildRecords = curKeySafe :: state.ChildRecords
                CurIndent = state.CurIndent
                Types = curType::state.Types
            }
        | JsonValue.Float(_) ->
            let curSb = 
                state.CurSb
                >~> ""
                >~> sprintf "%s///<summary>%s : <code>float</code></summary>" ~~2 (pathToType curPath)
                >~> sprintf "%smember this.%s = %s()" ~~2 curKeySafe (pathToType curPath)

            let curCodeSb =
                match state.Parent with
                | JsonValue.Record(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s:float" curKeySafe
                    else
                        sprintf "; %s:float" curKeySafe
                | _ ->
                    sprintf "float"

            let curJsonSb =
                if String.IsNullOrEmpty state.CurJsonSb then
                    sprintf "%slet %s = sprintf \"%sf\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe
                else
                    sprintf "\n%slet %s = sprintf \"%sf\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe

            let curType =
                ""
                >~> sprintf "%s///<summary>%s : <code>float</code></summary>" ~~1 (pathToType curPath)
                >~> sprintf "%stype %s() =" ~~1 (pathToType curPath)
                >~> sprintf "%sstatic member ToJson (o:float) =" ~~2
                >~> sprintf "%ssprintf \"%sf\" o" ~~3 "%"
                >~> ""
                >~> sprintf "%smember this.Set (o:float) =" ~~2
                >~> sprintf "%supdate 0 (sprintf \"%s\" (%s.ToJson o))" ~~3 (pathToJson curPath) (pathToType curPath)

            { state with
                CurSb = curSb
                CurCodeSb = state.CurCodeSb + curCodeSb
                CurJsonSb = state.CurJsonSb + curJsonSb
                ChildRecords = curKeySafe :: state.ChildRecords
                CurIndent = state.CurIndent
                Types = curType::state.Types
            }
        | JsonValue.Number(_) ->
            let curPath = addPath state.ParentPath curKey

            let curSb = 
                state.CurSb
                >~> ""
                >~> sprintf "%s///<summary>%s : <code>int</code></summary>" ~~2 (pathToType curPath)
                >~> sprintf "%smember this.%s = %s()" ~~2 curKeySafe (pathToType curPath)

            let curCodeSb =
                match state.Parent with
                | JsonValue.Record(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s:int" curKeySafe
                    else
                        sprintf "; %s:int" curKeySafe
                | _ ->
                    sprintf "int"

            let curJsonSb =
                if String.IsNullOrEmpty state.CurJsonSb then
                    sprintf "%slet %s = sprintf \"%si\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe
                else
                    sprintf "\n%slet %s = sprintf \"%si\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe

            let curType =
                ""
                >~> sprintf "%s///<summary>%s : <code>int</code></summary>" ~~1 (pathToType curPath)
                >~> sprintf "%stype %s() =" ~~1 (pathToType curPath)
                >~> sprintf "%sstatic member ToJson (o:int) =" ~~2
                >~> sprintf "%ssprintf \"%si\" o" ~~3 "%"
                >~> ""
                >~> sprintf "%smember this.Set (o:int) =" ~~2
                >~> sprintf "%supdate 0 (sprintf \"%s\" (%s.ToJson o))" ~~3 (pathToJson curPath) (pathToType curPath)

            { state with
                CurSb = curSb
                CurCodeSb = state.CurCodeSb + curCodeSb
                CurJsonSb = state.CurJsonSb + curJsonSb
                ChildRecords = curKeySafe :: state.ChildRecords
                CurIndent = state.CurIndent
                Types = curType::state.Types
            }
        | JsonValue.Boolean(_) ->
            let curPath = addPath state.ParentPath curKey

            let curSb = 
                state.CurSb
                >~> ""
                >~> sprintf "%s///<summary>%s : <code>bool</code></summary>" ~~2 (pathToType curPath)
                >~> sprintf "%smember this.%s = %s()" ~~2 curKeySafe (pathToType curPath)

            let curCodeSb =
                match state.Parent with
                | JsonValue.Record(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s:bool" curKeySafe
                    else
                        sprintf "; %s:bool" curKeySafe
                | _ ->
                    sprintf "bool"

            let curJsonSb =
                if String.IsNullOrEmpty state.CurJsonSb then
                    sprintf "%slet %s = sprintf \"%sb\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe
                else
                    sprintf "\n%slet %s = sprintf \"%sb\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe

            let curType =
                ""
                >~> sprintf "%s///<summary>%s : <code>bool</code></summary>" ~~1 (pathToType curPath)
                >~> sprintf "%stype %s() =" ~~1 (pathToType curPath)
                >~> sprintf "%sstatic member ToJson (o:bool) =" ~~2
                >~> sprintf "%ssprintf \"%sb\" o" ~~3 "%"
                >~> ""
                >~> sprintf "%smember this.Set (o:bool) =" ~~2
                >~> sprintf "%supdate 0 (sprintf \"%s\" (%s.ToJson o))" ~~3 (pathToJson curPath) (pathToType curPath)

            { state with
                CurSb = curSb
                CurCodeSb = state.CurCodeSb + curCodeSb
                CurJsonSb = state.CurJsonSb + curJsonSb
                ChildRecords = curKeySafe :: state.ChildRecords
                CurIndent = state.CurIndent
                Types = curType::state.Types
            }
        | JsonValue.Null ->            
            let curPath = addPath state.ParentPath curKey

            let curSb = 
                state.CurSb
                >~> ""
                >~> sprintf "%s///<summary>%s : <code>float</code></summary>" ~~2 (pathToType curPath)
                >~> sprintf "%smember this.%s = %s()" ~~2 curKeySafe (pathToType curPath)

            let curCodeSb =
                match state.Parent with
                | JsonValue.Record(_) ->
                    if String.IsNullOrEmpty state.CurCodeSb then
                        sprintf "%s:float" curKeySafe
                    else
                        sprintf "; %s:float" curKeySafe
                | _ ->
                    sprintf "float"

            let curJsonSb =
                if String.IsNullOrEmpty state.CurJsonSb then
                    sprintf "%slet %s = sprintf \"%sf\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe
                else
                    sprintf "\n%slet %s = sprintf \"%sf\" o.%s" ~~state.CurIndent curKeySafe "%" curKeySafe

            let curType =
                ""
                >~> sprintf "%s///<summary>%s : <code>float</code></summary>" ~~1 (pathToType curPath)
                >~> sprintf "%stype %s() =" ~~1 (pathToType curPath)
                >~> sprintf "%sstatic member ToJson (o:float) =" ~~2
                >~> sprintf "%ssprintf \"%sf\" o" ~~3 "%"
                >~> ""
                >~> sprintf "%smember this.Set (o:float) =" ~~2
                >~> sprintf "%supdate 0 (sprintf \"%s\" (%s.ToJson o))" ~~3 (pathToJson curPath) (pathToType curPath)

            { state with
                CurSb = curSb
                CurCodeSb = state.CurCodeSb + curCodeSb
                CurJsonSb = state.CurJsonSb + curJsonSb
                ChildRecords = curKeySafe :: state.ChildRecords
                CurIndent = state.CurIndent
                Types = curType::state.Types
            }

    let fig = JsonValue.Parse json

    let nextState = getTypes { ParentPath = []; Parent = fig; CurSb = ""; CurCodeSb = ""; CurJsonSb = ""; ChildRecords = []; CurIndent = 3; Types = [] } ("Figure",fig)
    let typeStr =
        nextState.Types
        |> List.rev
        |> List.fold (>~>) ""

    let sb = StringBuilder()
    sb.Clear() |> ignore
    sb.AppendLine(sprintf "namespace %s\n\n" namespaceName) |> ignore
    sb.AppendLine(sprintf "module %s =" moduleName) |> ignore
    sb.AppendLine("    open Server") |> ignore
    sb.AppendLine(typeStr) |> ignore

    sb.ToString()
    

let storeSchema (filename:string) str =
    if Directory.Exists(Path.GetDirectoryName(filename)) |> not then
        Directory.CreateDirectory(Path.GetDirectoryName(filename)) |> ignore
    
    File.WriteAllText(filename, str)


[<EntryPoint>]
let main argv =
    let chartType = "HighCharts"
    let json = loadJson (sprintf "%s.json" chartType)

    //let json = "{\"title\":\"Title\",\"xaxis\":{\"name\":\"x\",\"font\":\"arial\"},\"range\":[0.0,10.0],\"data\":[[{\"t\":0,\"v\":0.0,\"point\":{\"name\":\"a1\"}},{\"t\":1,\"v\":1.0,\"point\":{\"name\":\"b1\"}}],[{\"t\":0,\"v\":2.0,\"point\":{\"name\":\"a2\"}},{\"t\":1,\"v\":3.0,\"point\":{\"name\":\"b2\"}}],[{\"t\":0,\"v\":4.0,\"point\":{\"name\":\"a3\"}},{\"t\":1,\"v\":5.0,\"point\":{\"name\":\"b3\"}}]],\"legend\":[{\"label\":\"one\"},{\"label\":\"two\"}]}"
    let fsStr = jsonToSchema (sprintf "FPlot.%s" chartType) "Figure" json

    printfn "Generating F# code from JSON %s object..." chartType
    storeSchema (sprintf "./FPlot/%s.Figure.fs" chartType) fsStr

    0
