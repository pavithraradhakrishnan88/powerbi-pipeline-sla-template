let
    Source = Csv.Document(File.Contents(DataFolder & "\Dim_Category.csv"),[Delimiter=",", Columns=3, Encoding=1252, QuoteStyle=QuoteStyle.None]),
    #"Promoted Headers" = Table.PromoteHeaders(Source, [PromoteAllScalars=true]),
    #"Changed Type" = Table.TransformColumnTypes(#"Promoted Headers",{{"CategoryID", Int64.Type}, {"CategoryName", type text}, {"SortOrder", Int64.Type}})
in
    #"Changed Type"