#r "System.Data.dll"
#r "Google.DataTable.Net.Wrapper.dll"
#r "XPlot.GoogleCharts.dll"
#r "XPlot.Plotly.dll"

open System
open FSharp.Data
open Google.DataTable.Net.Wrapper
open XPlot.GoogleCharts
open XPlot.Plotly
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs.Host
open System.Data
open System.Threading.Tasks

type data = {
    label: string
    value: float
}

let Run (input: string, log: TraceWriter) =  

    log.Info(sprintf "Generate chart")

    let mutable Bolivia = [("2015/2016",2);("2016/2017",2);("2017/2018",19)]
    let mutable Ecuador = [("2015/2016",6);("2016/2017",42);("2017/2018",3)]
    let mutable Madagascar = [("2015/2016",11);("2016/2017",22);("2017/2018",2)]
    let mutable Average = [("2015/2016",31);("2016/2017",3);("2017/2018",14)]

    let series = [ "bars"; "bars"; "bars"; "lines" ]
    let inputs = [ Bolivia; Ecuador; Madagascar; Average ]

    let output =
        inputs
        |> Chart.Combo
        |> Chart.WithOptions 
            (Options(title = "Coffee Production", series = 
                [| for typ in series -> Series(typ) |]))
        |> Chart.WithLabels 
            ["Bolivia"; "Ecuador"; "Madagascar"; "Average"]
        |> Chart.WithLegend true
        |> Chart.WithSize (600, 250)   

    log.Info(output.Html)
    log.Info(input)
