<?php
  $input = fgets(STDIN);
  $input = rtrim($input, "\n\r");
  
  $output = sprintf("PHP script processed queue message '$input'", $string);
  
  fwrite(STDOUT, $output);
?>