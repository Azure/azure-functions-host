import os

# read the input and write to stdout
input = open(os.environ['input']).read()
message = "Python script processed queue message '{0}'".format(input)
print(input)
