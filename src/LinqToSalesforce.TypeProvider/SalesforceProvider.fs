﻿namespace ProviderImplementation

open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System
open System.IO
open System.Net
open Newtonsoft.Json
open LinqToSalesforce
open Rest
open Rest.OAuth
open System.ComponentModel
open System.Runtime.Caching

type TableContext = SoqlContext*string

module RestApi =
  let cache = new MemoryCache("RestApi")
  let cacheAndReturns key (f:unit -> 't) =
    if cache.Contains key
    then 
      cache.Get(key) :?> 't
    else
      let result = f()
      let policy = new CacheItemPolicy()
      policy.SlidingExpiration <- TimeSpan.FromMinutes 5.
      cache.Add(key, result, policy) |> ignore
      result
  let loadTableList oauth (f:string -> unit) =
    async {
      let! tableNames = cacheAndReturns "tableNames" (fun () -> getObjectsDescUrls oauth)
      //let! tableNames = getObjectsDescUrls oauth // |> Seq.take 5 // |> Async.RunSynchronously
      for name in tableNames do
        f name
    }

type BaseEntity () =
  let event = Event<_, _>()
//  let mutable propval = 0.0
//  member this.MyProperty
//    with get() = propval
//    and  set(v) =
//        propval <- v
//        event.Trigger(this, new PropertyChangedEventArgs("MyProperty"))
  interface INotifyPropertyChanged with
    member this.add_PropertyChanged(e) =
        event.Publish.AddHandler(e)
    member this.remove_PropertyChanged(e) =
        event.Publish.RemoveHandler(e)
  interface ISalesforceEntity with
    member val Id="" with get,set
    //[<CLIEvent>]
    //member this.PropertyChanged: IEvent<PropertyChangedEventHandler,PropertyChangedEventArgs> = 
    //  raise (NotImplementedException())
    member this.TrackPropertyUpdates(): unit = 
      raise (NotImplementedException())
    member this.UpdatedProperties: Collections.Generic.IDictionary<string,obj> = 
      raise (NotImplementedException())
    

[<TypeProvider>]
type SalesforceProvider () as this =
  inherit TypeProviderForNamespaces ()
  let ns = "SalesforceProvider"
  let asm = Assembly.GetExecutingAssembly()
  let tyName = "SalesforceTypeProvider"
  let myType = ProvidedTypeDefinition(asm, ns, tyName, None)

  do myType.DefineStaticParameters(
    [ ProvidedStaticParameter("authFile", typeof<string>)
      ProvidedStaticParameter("instanceName", typeof<string>, "") ],
      fun typeName [|:? string as authFile; :? string as instanceName|] ->
        let ty = ProvidedTypeDefinition(asm, ns, typeName, None)
        if not <| String.IsNullOrWhiteSpace instanceName
        then Config.ProductionInstance <- instanceName

        let authJson = File.ReadAllText authFile
        let authparams = ImpersonationParam.FromJson authJson
        let oauth = authenticateWithCredentials authparams |> Async.RunSynchronously
      
        ProvidedConstructor([], 
              InvokeCode=(
                fun _ -> 
                  <@@ 
                    let imperso = authJson |> ImpersonationParam.FromJson 
                    let id = imperso |> authenticateWithCredentials |> Async.RunSynchronously
                    SoqlContext(instanceName, imperso)
                  @@>
                ))
          |> ty.AddMember
      
        let tablesType = ProvidedTypeDefinition(
                            "TablesType", 
                            baseType = Some typeof<obj>,
                            HideObjectMethods = true)
        ProvidedConstructor([],
            InvokeCode=
                fun [c]-> 
                    <@@
                        let tables = %%c:TableDesc array
                        tables
                    @@>
            ) |> tablesType.AddMember
        do ty.AddMember tablesType

        do ProvidedProperty("Tables", tablesType,
              GetterCode = fun args ->
                  <@@
                      let a = (%%args.[0]:obj) :?> SoqlContext
                      a
                  @@>) |> ty.AddMember
        
        RestApi.loadTableList oauth (
          fun table ->
  //          ProvidedProperty(CodeGeneration.pluralize table, typeof<obj>, //tableType,
  //            GetterCode=fun args -> 
  //              <@@
  //                let ctx = (%%args.[0]:>obj) :?> SoqlContext
  ////                let id = (%%args.[0]:>obj) :?> Identity
  //                (ctx,table)
  //              @@>
  //              ) |> tablesType.AddMember
                tablesType.AddMemberDelayed (fun () ->
                  ProvidedProperty(CodeGeneration.pluralize table, typeof<obj>, //tableType,
                    GetterCode=fun args -> 
                      <@@
                        let ctx = (%%args.[0]:>obj) :?> SoqlContext
                        (ctx,table)
                      @@>
                      )
                  )
        ) |> Async.StartAsTask |> ignore
        
  //      async {
  //        let! tableNames = getObjectsDescUrls oauth // |> Seq.take 5 // |> Async.RunSynchronously
  //        for name in tableNames do
  ////          let entityType = ProvidedTypeDefinition(
  ////                              sprintf "%sEntity" name,
  ////                              baseType = Some typeof<BaseEntity>,
  ////                              HideObjectMethods = false)
  ////          for field in table.Fields do
  ////            let fn = field.Name
  ////            ProvidedProperty(field.Name, typeof<string>,
  ////              GetterCode=fun args -> 
  ////                <@@
  //////                  let id = (%%args.[0]:>obj) :?> Identity
  //////                  (id,name)
  ////                  fn
  ////                @@>) |> entityType.AddMember
  ////          
  ////          let tableType = ProvidedTypeDefinition(
  ////                              sprintf "%sTable" table.Name,
  ////                              baseType = Some typeof<TableContext>,
  ////                              HideObjectMethods = true)
  ////          ProvidedConstructor([],
  ////              InvokeCode=
  ////                  fun [c]-> 
  ////                      <@@
  ////                          let ctx = %%c:TableContext
  ////                          ctx
  ////                      @@>
  ////              ) |> tableType.AddMember
  ////          do ty.AddMember tableType

  ////          ProvidedProperty("Query", typeof<TableContext>,
  ////            GetterCode=fun args -> 
  ////              <@@
  ////                let ctx = (%%args.[0]:>obj)// :?> TableContext
  ////                ctx
  ////              @@>
  ////              ) |> tableType.AddMember
  //          //Queryable

  ////          let name = table.Name
  //          ProvidedProperty(CodeGeneration.pluralize name, typeof<obj>, //tableType,
  //            GetterCode=fun args -> 
  //              <@@
  //                let ctx = (%%args.[0]:>obj) :?> SoqlContext
  ////                let id = (%%args.[0]:>obj) :?> Identity
  //                (ctx,name)
  //              @@>
  //              ) |> tablesType.AddMember
              
  //      } |> Async.StartImmediate

        ty)


  do this.AddNamespace(ns, [myType])

[<TypeProviderAssembly>]
do ()