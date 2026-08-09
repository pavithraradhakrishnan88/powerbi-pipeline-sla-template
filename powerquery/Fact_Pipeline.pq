let
    Source = Csv.Document(File.Contents(DataFolder & "\Fact_Pipeline_SampleData.csv"),[Delimiter=",", Columns=6, Encoding=1252, QuoteStyle=QuoteStyle.None]),
    #"Promoted Headers" = Table.PromoteHeaders(Source, [PromoteAllScalars=true]),
    #"Changed Type" = Table.TransformColumnTypes(#"Promoted Headers",{{"PipelineID", type text}, {"StartDate", type datetime}, {"EndDate", type datetime}, {"Category", type text}, {"SLA_Target_Hrs", Int64.Type}, {"Status", type text}})
in
    #"Changed Type"