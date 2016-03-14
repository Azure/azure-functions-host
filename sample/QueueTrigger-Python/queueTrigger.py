import os
import json

# read the queue message and write to stdout
input = open(os.environ['input']).read()
message = "Python script processed queue message '{0}'".format(input)
print(message)

# read the entities from the table binding
tableData = open(os.environ['tableInput'])
table = json.load(tableData)
print("Read {0} Table entities".format(len(table)))
for entity in table:
  print(entity)

# write to the output binding
f = open(os.environ['output'], 'w')
f.write(input)
