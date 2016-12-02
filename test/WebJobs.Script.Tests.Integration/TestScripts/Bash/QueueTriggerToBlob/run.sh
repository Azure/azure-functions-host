input=$(<$input)
printf "Bash script processed queue message: $input" 
echo $input >> $output